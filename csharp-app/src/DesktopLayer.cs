using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AnimeWidget;

/// <summary>
/// 桌面层：把窗口挂到壁纸层（WorkerW）或置底（BottomPin），运行时可切换。
/// 移植自 Rust 版 win32.rs。另含：鼠标穿透、亚克力、原生缩放。
/// </summary>
public class DesktopLayer
{
    private readonly IntPtr _hwnd;
    public EmbedMode Mode { get; private set; } = EmbedMode.Normal;

    public DesktopLayer(IntPtr hwnd, EmbedMode want)
    {
        _hwnd = hwnd;
        SetMode(want);
    }

    /// <summary>显示状态下重新 assert 层级（托盘「显示」后调用，防被桌面格子/整理软件盖住）。</summary>
    public void EnsureVisible()
    {
        if (!Win32.IsWindow(_hwnd)) return;
        switch (Mode)
        {
            case EmbedMode.WorkerW:
                Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
                Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
                break;
            case EmbedMode.BottomPin:
                Win32.SetWindowPos(_hwnd, new IntPtr(1), 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
                Win32.ShowWindow(_hwnd, Win32.SW_SHOWNA);
                break;
        }
    }

    /// <summary>WorkerW 父层被重建/换柄后重新挂接（看门狗周期性调用）。</summary>
    public void EnsureParented()
    {
        if (Mode != EmbedMode.WorkerW) return;
        if (!Win32.IsWindow(_hwnd)) return; // 句柄已随父层销毁，WPF 层无法自救
        var workerw = FindWorkerW();
        if (workerw == IntPtr.Zero) return; // 桌面层重建中，下轮再试
        if (Win32.GetParent(_hwnd) != workerw)
            SetMode(EmbedMode.WorkerW); // 父层被换 → 重新挂接
    }

    public void SetMode(EmbedMode mode)
    {
        switch (mode)
        {
            case EmbedMode.WorkerW:
                var workerw = FindWorkerW();
                if (workerw != IntPtr.Zero)
                {
                    long style = Win32.GetStyle(_hwnd);
                    Win32.SetStyle(_hwnd, (style & ~Win32.WS_POPUP) | Win32.WS_CHILD);
                    if (Win32.SetParent(_hwnd, workerw) != IntPtr.Zero)
                    {
                        Mode = EmbedMode.WorkerW;
                        // 提到兄弟窗口顶层：iTop/酷呆的桌面格子也挂在这层，别被它们盖住
                        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
                        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
                        return;
                    }
                }
                ForceBottomPin(); // 找不到/挂不上 → 降级置底
                break;

            case EmbedMode.BottomPin:
                ForceBottomPin();
                break;

            default:
                ForceNormal();
                break;
        }
    }

    /// <summary>普通窗口：完全不碰 Progman/WorkerW，桌面整理软件零冲突。</summary>
    private void ForceNormal()
    {
        long style = Win32.GetStyle(_hwnd);
        Win32.SetStyle(_hwnd, (style & ~Win32.WS_CHILD) | Win32.WS_POPUP);
        Win32.SetParent(_hwnd, IntPtr.Zero);
        Mode = EmbedMode.Normal;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED);
        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
    }

    private void ForceBottomPin()
    {
        long style = Win32.GetStyle(_hwnd);
        Win32.SetStyle(_hwnd, (style & ~Win32.WS_CHILD) | Win32.WS_POPUP);
        Win32.SetParent(_hwnd, IntPtr.Zero);
        Mode = EmbedMode.BottomPin;
        // HWND_BOTTOM(1) 压到最底，普通窗口一开就盖住它
        Win32.SetWindowPos(_hwnd, new IntPtr(1), 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
        Win32.ShowWindow(_hwnd, Win32.SW_SHOWNA);
    }

    /// <summary>标准套路：给 Progman 发 0x052C 让它分裂出壁纸层 WorkerW。
    /// 关键：只接受归属 explorer.exe 的 WorkerW——iTop/酷呆自建的 WorkerW 属于它们自己，
    /// 挂进去父层被重建时会连坐销毁我们的 hwnd（僵尸窗口，只能重启，用户实测"切不回来"）。</summary>
    private static IntPtr FindWorkerW()
    {
        var progman = Win32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        Win32.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        IntPtr found = IntPtr.Zero;
        Win32.EnumWindows((top, _) =>
        {
            if (Win32.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                var workerw = Win32.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                if (workerw != IntPtr.Zero && Win32.IsExplorerOwned(workerw))
                {
                    found = workerw;
                    return false; // 停止枚举
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}

internal static class Win32
{
    // ---- 样式 ----
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_EX_LAYERED = 0x80000;
    public const long WS_EX_TRANSPARENT = 0x20;

    public const int SW_SHOW = 5;
    public const int SW_SHOWNA = 8;
    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOMOVE = 0x2;
    public const uint SWP_NOZORDER = 0x4;
    public const uint SWP_FRAMECHANGED = 0x20;
    public const uint SWP_NOACTIVATE = 0x10;
    public const uint SWP_SHOWWINDOW = 0x40;

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static long GetStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
    public static void SetStyle(IntPtr hwnd, long style) => SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
    public static long GetExStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
    public static void SetExStyle(IntPtr hwnd, long style) => SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));

    public static void SetClickThrough(IntPtr hwnd, bool on)
    {
        long s = GetExStyle(hwnd) | WS_EX_LAYERED;
        s = on ? (s | WS_EX_TRANSPARENT) : (s & ~WS_EX_TRANSPARENT);
        SetExStyle(hwnd, s);
    }

    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public const uint GA_ROOT = 2;
    public const int VK_LBUTTON = 0x01;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    /// <summary>窗口是否归属 explorer.exe（WorkerW 挂接前的防连坐校验）。</summary>
    public static bool IsExplorerOwned(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName
                .Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static void BeginNativeResize(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTBOTTOMRIGHT), IntPtr.Zero);
    }

    // ---- 亚克力 ----

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;      // 4 = ACCENT_ENABLE_ACRYLICBLURBEHIND
        public int AccentFlags;
        public int GradientColor;    // ABGR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;        // 19 = WCA_ACCENT_POLICY
        public IntPtr Data;
        public int SizeOfData;
    }

    public static void EnableAcrylic(IntPtr hwnd) => SetAccent(hwnd, 4, 2, unchecked((int)0x381E1410)); // 22% 轻 tint
    public static void DisableAcrylic(IntPtr hwnd) => SetAccent(hwnd, 0, 0, 0); // ACCENT_DISABLED

    private static void SetAccent(IntPtr hwnd, int state, int flags, int gradientColor)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = flags,
                GradientColor = gradientColor,
                AnimationId = 0,
            };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(ptr);
        }
        catch { /* 降级：纯半透明 */ }
    }
}
