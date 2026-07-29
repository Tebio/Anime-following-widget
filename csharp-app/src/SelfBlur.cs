using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnimeWidget;

/// <summary>
/// 自截屏磨砂：抓窗口矩形背后的屏幕内容 → 半分辨率盒式模糊 → 深色 tint 烘焙 → 冻结位图。
/// 为什么不用系统 API：ACCENT/DWMWA_SYSTEMBACKDROP 都作用于整个窗口矩形，
/// 对 AllowsTransparency 的圆角卡片窗口要么静默失效、要么在卡片后面多个矩形底
/// （用户实测"在原有背景下面再加个底"）。自绘是唯一形状精确、全系统版本可用的路线。
/// 代价：抓取前要藏窗一帧（~30ms 微闪），与酷呆等 duilib 应用的截屏模糊同款取舍。
/// </summary>
public static class SelfBlur
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);

    private const uint SRCCOPY = 0x00CC0020;
    private const int HALFTONE = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPpm, YPpm, ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER Header; public int Colors; }

    /// <summary>抓 hwnd 矩形区域背后的屏幕，模糊+ tint 后返回冻结位图；失败返回 null（调用方回退纯色）。
    /// v3.11.1：用 WDA_EXCLUDEFROMCAPTURE 让截屏原生跳过自己——全程不藏窗，
    /// 无闪烁、z-order 不动（v3.11.0 藏窗法：闪烁 + 重新 Show 后跳到最前，两个实测问题）。
    /// 亲和性设置失败（老系统）时回退藏窗法。</summary>
    internal static BitmapSource? CaptureBlurred(IntPtr hwndHide, Win32.RECT rect)
    {
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w < 16 || h < 16) return null;
        int sw = Math.Clamp(w / 2, 4, 720), sh = Math.Clamp(h / 2, 4, 720);

        bool affinity = Win32.SetWindowDisplayAffinity(hwndHide, Win32.WDA_EXCLUDEFROMCAPTURE);
        if (!affinity) Win32.ShowWindow(hwndHide, 0); // 回退：藏窗（SW_HIDE）
        try
        {
            System.Threading.Thread.Sleep(50); // 等 DWM 按新亲和性/藏窗重组一帧（屏幕上无可见变化）
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return null;
            var mem = CreateCompatibleDC(screen);
            var bmp = CreateCompatibleBitmap(screen, sw, sh);
            var old = SelectObject(mem, bmp);
            SetStretchBltMode(mem, HALFTONE);
            StretchBlt(mem, 0, 0, sw, sh, screen, rect.Left, rect.Top, w, h, SRCCOPY);

            var bmi = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = sw, Height = -sh, // 负高 = 自上而下
                    Planes = 1, BitCount = 32,
                },
            };
            var data = new byte[sw * sh * 4];
            GetDIBits(mem, bmp, 0, (uint)sh, data, ref bmi, 0);

            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);

            BoxBlur(data, sw, sh, 6);
            BoxBlur(data, sw, sh, 6);

            // 深色 tint 烘焙（55% #141D18 系）：通透感 + 白字可读性
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                data[i] = (byte)(data[i] * 0.45 + 0x18 * 0.55);     // B
                data[i + 1] = (byte)(data[i + 1] * 0.45 + 0x20 * 0.55); // G
                data[i + 2] = (byte)(data[i + 2] * 0.45 + 0x14 * 0.55); // R
                data[i + 3] = 255;
            }

            var wb = new WriteableBitmap(sw, sh, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, sw, sh), data, sw * 4, 0);
            wb.Freeze();
            return wb;
        }
        catch { return null; }
        finally
        {
            if (affinity) Win32.SetWindowDisplayAffinity(hwndHide, Win32.WDA_NONE);
            else Win32.ShowWindow(hwndHide, 8); // SW_SHOWNA
        }
    }

    /// <summary>滑动窗口盒式模糊：横向一遍 + 纵向一遍。</summary>
    private static void BoxBlur(byte[] d, int w, int h, int r)
    {
        var tmp = new byte[d.Length];
        // 横向
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int c = 0; c < 4; c++)
            {
                int acc = 0, cnt = 0;
                for (int x = -r; x <= r; x++) { int xx = Math.Clamp(x, 0, w - 1); acc += d[row + xx * 4 + c]; cnt++; }
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x * 4 + c] = (byte)(acc / cnt);
                    int xAdd = Math.Clamp(x + r + 1, 0, w - 1), xSub = Math.Clamp(x - r, 0, w - 1);
                    acc += d[row + xAdd * 4 + c] - d[row + xSub * 4 + c];
                }
            }
        }
        // 纵向
        for (int x = 0; x < w; x++)
        {
            for (int c = 0; c < 4; c++)
            {
                int acc = 0, cnt = 0;
                for (int y = -r; y <= r; y++) { int yy = Math.Clamp(y, 0, h - 1); acc += tmp[yy * w * 4 + x * 4 + c]; cnt++; }
                for (int y = 0; y < h; y++)
                {
                    d[y * w * 4 + x * 4 + c] = (byte)(acc / cnt);
                    int yAdd = Math.Clamp(y + r + 1, 0, h - 1), ySub = Math.Clamp(y - r, 0, h - 1);
                    acc += tmp[yAdd * w * 4 + x * 4 + c] - tmp[ySub * w * 4 + x * 4 + c];
                }
            }
        }
    }
}
