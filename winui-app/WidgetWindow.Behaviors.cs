using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using AnimeWidget;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace AnimeWidget.WinUI;

public partial class WidgetWindow
{
// ---------- Win32：鼠标穿透 ----------

    private void ApplyClickThrough(bool on) => SetExStyle(WS_EX_TRANSPARENT, on);

// ---------- 原生拖拽/缩放区域（InputNonClientPointerSource） ----------

    private Microsoft.UI.Input.InputNonClientPointerSource? _ncpSource;

    private void UpdateInputRegions()
    {
        if (_ncpSource == null) return;
        try
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            int w = (int)(RootGrid.ActualWidth * scale), h = (int)(RootGrid.ActualHeight * scale);
            if (w <= 0 || h <= 0) return;

            // 全窗口为 Caption 拖拽区；交互控件设 Passthrough 保证可点
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption,
                _settings.Locked ? Array.Empty<RectInt32>() : new[] { new RectInt32(0, 0, w, h) });

            var pass = new List<RectInt32>();
            foreach (var el in new FrameworkElement[] { HeaderButtons, TabPanel, SearchBox, EntryList })
            {
                var p = el.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
                pass.Add(new RectInt32(
                    (int)(p.X * scale), (int)(p.Y * scale),
                    (int)(el.ActualSize.X * scale), (int)(el.ActualSize.Y * scale)));
            }
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, pass.ToArray());

            // 四边缩放区（8px；角落由相邻边交汇自动生效，SDK 无单独角落枚举）
            const int edge = 8;
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.LeftBorder, new[] { new RectInt32(0, 0, edge, h) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.RightBorder, new[] { new RectInt32(w - edge, 0, edge, h) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.TopBorder, new[] { new RectInt32(0, 0, w, edge) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.BottomBorder, new[] { new RectInt32(0, h - edge, w, edge) });
        }
        catch { }
    }

    // ---------- 圆角（Win10 无 DWM 圆角 API，用窗口 Region） ----------

    private void ApplyRoundedRegion()
    {
        try
        {
            int cornerPref = 2; // DWMWCP_ROUND —— Win11 原生圆角
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, 4);
            // Win10 回落：SetWindowRgn 圆角裁剪
            if (Environment.OSVersion.Version.Build < 22000)
            {
                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
                int w = (int)(RootGrid.ActualWidth * scale), h = (int)(RootGrid.ActualHeight * scale);
                int r = (int)(10 * scale);
                if (w > 0 && h > 0)
                {
                    IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r);
                    SetWindowRgn(_hwnd, rgn, true);
                }
            }
        }
        catch { }
    }

    // ---------- 贴边磁吸 + 出屏回弹（轮询版：位置停稳 400ms 后执行） ----------

    private bool _snapping;
    private PointInt32 _lastPos = new(-1, -1);
    private DateTime _lastMoveAt = DateTime.MinValue;

    private void SnapTick()
    {
        if (_edgeHidden) return;
        try
        {
            var pos = AppWindow.Position;
            if (pos.X != _lastPos.X || pos.Y != _lastPos.Y)
            {
                _lastPos = pos;
                _lastMoveAt = DateTime.UtcNow;
                return; // 还在动
            }
            if (_snapping || (DateTime.UtcNow - _lastMoveAt).TotalMilliseconds < 400 || _lastMoveAt == DateTime.MinValue)
                return;

            var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var size = AppWindow.Size;
            const int snap = 16;
            int nx = pos.X, ny = pos.Y;

            // 出屏回弹：超过一半在可视区外 → 拉回
            int visibleW = Math.Min(pos.X + size.Width, wa.X + wa.Width) - Math.Max(pos.X, wa.X);
            int visibleH = Math.Min(pos.Y + size.Height, wa.Y + wa.Height) - Math.Max(pos.Y, wa.Y);
            if (visibleW < size.Width / 2 || visibleH < size.Height / 2)
            {
                nx = Math.Clamp(pos.X, wa.X, wa.X + wa.Width - size.Width);
                ny = Math.Clamp(pos.Y, wa.Y, wa.Y + wa.Height - size.Height);
            }
            else
            {
                // 贴边磁吸 16px
                if (Math.Abs(pos.X - wa.X) < snap) nx = wa.X;
                else if (Math.Abs(pos.X + size.Width - (wa.X + wa.Width)) < snap) nx = wa.X + wa.Width - size.Width;
                if (Math.Abs(pos.Y - wa.Y) < snap) ny = wa.Y;
                else if (Math.Abs(pos.Y + size.Height - (wa.Y + wa.Height)) < snap) ny = wa.Y + wa.Height - size.Height;
            }
            if (nx != pos.X || ny != pos.Y)
            {
                _snapping = true;
                AppWindow.Move(new PointInt32(nx, ny));
                _lastPos = new PointInt32(nx, ny);
                _snapping = false;
            }
        }
        catch { }
    }

    // ---------- 悬停显示 / 贴边隐藏（光标轮询） ----------

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pollTimer;
    private bool _hoverHidden;
    private bool _edgeHidden;
    private PointInt32 _preHidePos;
    private bool _preHideValid;

    private void PollCursor()
    {
        try
        {
            GetCursorPos(out var cur);
            var pos = AppWindow.Position; var size = AppWindow.Size;
            bool inside = cur.X >= pos.X && cur.X <= pos.X + size.Width
                       && cur.Y >= pos.Y && cur.Y <= pos.Y + size.Height;

            // 悬停显示：平时 Hide，光标进入区域浮现（3.16.1 同款语义：被遮挡也浮现）。
            // 用 AppWindow.Hide/Show 不用分层 alpha（alpha+DComp 在 Win10 上有概率整个窗消失）。
            // 设置窗开着时强制可见（3.16.1 行为：一边调设置一边看效果，别在眼皮底下消失）。
            if (_settings.HoverReveal)
            {
                bool settingsOpen = _settingsWin != null;
                if ((inside || settingsOpen) && _hoverHidden)
                {
                    _hoverHidden = false;
                    _visible = true;
                    AppWindow.Show();
                }
                else if (!inside && !settingsOpen && !_hoverHidden)
                {
                    _hoverHidden = true;
                    _visible = false;
                    AppWindow.Hide();
                }
            }
            else if (_hoverHidden)
            {
                _hoverHidden = false;
                _visible = true;
                AppWindow.Show();
            }

            // 贴边隐藏：贴着屏幕边且光标远离 → 缩成 6px 细条；光标靠近 → 滑回
            if (_settings.EdgeHide && !_settings.Locked)
            {
                var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                bool atRight = pos.X + size.Width >= wa.X + wa.Width - 2;
                bool atLeft = pos.X <= wa.X + 2;
                bool atTop = pos.Y <= wa.Y + 2;
                bool near = cur.X >= pos.X - 24 && cur.X <= pos.X + size.Width + 24
                         && cur.Y >= pos.Y - 24 && cur.Y <= pos.Y + size.Height + 24;

                if (!_edgeHidden && (atRight || atLeft || atTop) && !near)
                {
                    _edgeHidden = true;
                    _preHidePos = pos; _preHideValid = true;
                    const int strip = 6;
                    if (atRight) AppWindow.Move(new PointInt32(wa.X + wa.Width - strip, pos.Y));
                    else if (atLeft) AppWindow.Move(new PointInt32(wa.X + strip - size.Width, pos.Y));
                    else AppWindow.Move(new PointInt32(pos.X, wa.Y + strip - size.Height));
                }
                else if (_edgeHidden && near)
                {
                    _edgeHidden = false;
                    if (_preHideValid) AppWindow.Move(_preHidePos);
                }
            }
            else if (_edgeHidden)
            {
                _edgeHidden = false;
                if (_preHideValid) AppWindow.Move(_preHidePos);
            }
        }
        catch { }
    }

    private byte CurrentNormalAlpha() =>
        _settings.BlurMode == 0
            ? (byte)Math.Clamp(_settings.WindowOpacity * 255, 60, 255)
            : (byte)255;

    private static bool IsShellWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(64);
        _ = GetClassNameW(hwnd, sb, sb.Capacity);
        var cls = sb.ToString();
        return cls is "Progman" or "WorkerW" or "SHELLDLL_DefView";
    }
}
