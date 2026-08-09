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

/// <summary>列表行显示模型（Entry + 展示态）。</summary>
public sealed class RowModel
{
    public required string Title { get; init; }
    public required string Meta { get; init; }
    public required string DetailId { get; init; }
    public required string Star { get; set; }
    public bool IsEnd { get; init; }
    public Visibility IsNewVis { get; init; }
    public required string Url { get; init; }
}

public partial class WidgetWindow : Window
{
    private readonly ScheduleService _sched = new();
    private readonly AppSettings _settings;
    private readonly ObservableCollection<RowModel> _rows = new();
    private readonly List<Button> _tabButtons = new();
    private int _selectedDay;
    private string _search = "";
    private WeekSchedule? _view;
    private SettingsWindow? _settingsWin;
    private readonly IntPtr _hwnd;
    private bool _visible = true;

    public WidgetWindow()
    {
        BootLog.Log("WidgetWindow.ctor enter");
        InitializeComponent();
        BootLog.Log("XamlInit ok");

        _settings = AppSettings.Load();
        _hwnd = WindowNative.GetWindowHandle(this);

        // ---- 深色主题（WinUI3 里 Application.RequestedTheme 靠不住，必须在根元素上设） ----
        RootGrid.RequestedTheme = ElementTheme.Dark;

        // ---- 材质（设置里可切：0透明卡片/1毛玻璃/2亚克力） ----
        BootLog.Log("Backdrop: " + BackdropHelper.ApplyMaterial(this, _settings.BlurMode, _settings.BgDarkness));

        // ---- 卸系统框 + 手动拖拽（SetTitleBar 在无标题框下会失效，改 WM_NCLBUTTONDOWN 手动拖） ----
        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false); // 否则失焦时右上角浮出原生 X（"两个X"根因）
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        // 边框样式：保留 WS_THICKFRAME（系统边缘缩放需要它），清 DLGFRAME/BORDER（白边根因）
        uint st = (uint)GetWindowLongPtrW(_hwnd, GWL_STYLE);
        _ = SetWindowLongPtrW(_hwnd, GWL_STYLE, (IntPtr)(ulong)((st | WS_THICKFRAME) & ~(WS_DLGFRAME | WS_BORDER)));
        AppWindow.Resize(new SizeInt32(360, 540));

        // ---- 原生级拖拽+八向缩放：NonClientPointerSource 区域（无标题栏窗口的正解） ----
        _ncpSource = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        RootGrid.SizeChanged += (_, _) => { UpdateInputRegions(); ApplyRoundedRegion(); };
        TabPanel.Loaded += (_, _) => UpdateInputRegions();

        // ---- 贴边磁吸：拖动松手后吸附屏幕边 ----
        _snapTimer = DispatcherQueue.CreateTimer();
        _snapTimer.Interval = TimeSpan.FromMilliseconds(350);
        _snapTimer.IsRepeating = false;
        _snapTimer.Tick += (_, _) => SnapToEdges();
        AppWindow.Changed += (_, a) => { if (a.DidPositionChange && !_snapping) { _snapTimer.Stop(); _snapTimer.Start(); } };

        // ---- 悬停显示/贴边隐藏：光标轮询（不依赖窗口消息，被遮挡也能探到） ----
        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(150);
        _pollTimer.Tick += (_, _) => PollCursor();
        _pollTimer.Start();
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var wa = area.WorkArea;
            AppWindow.Move(new PointInt32(wa.X + wa.Width - 360 - 24, wa.Y + 80));
        }
        catch { }

        EntryList.ItemsSource = _rows;
        BootLog.Log("ctor done");

        // ---- 周几 tab ----
        _selectedDay = ScheduleService.TodayIndex();
        for (int i = 0; i < 7; i++)
        {
            var b = new Button
            {
                Content = ScheduleService.WeekdayNames[i],
                Padding = new Thickness(7, 2, 7, 2),
                FontSize = 10.5,
                Tag = i,
            };
            b.Click += Tab_Click;
            _tabButtons.Add(b);
            TabPanel.Children.Add(b);
        }

        // ---- 数据 ----
        _sched.ScheduleUpdated += _ => DispatcherQueue.TryEnqueue(() => BindSchedule());
        _sched.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateStatus);
        BindSchedule(AppSettings.LoadCache());
        _sched.Start(_settings.RefreshMinutes);

        ApplySettings();
        TrayIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleVisibility);
        TrayIcon.ForceCreate();
        Closed += (_, _) => _sched.Dispose();
    }

    // ---------- 设置应用 ----------

    public void ApplySettings()
    {
        BackdropHelper.ApplyMaterial(this, _settings.BlurMode, _settings.BgDarkness, _settings.WindowOpacity);
        var bg = BackdropHelper.BgColor(_settings.BgDarkness);
        if (_settings.BlurMode == 0)
        {
            // 透明卡片：WinUI3 无 backdrop 时 swapchain 不透明，XAML 半透明刷无效 ——
            // 必须分层窗口整体 alpha（WPF 3.16.1 同款机制），文字随窗口统一通透
            RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, bg.R, bg.G, bg.B));
            SetExStyle(WS_EX_LAYERED, true);
            SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Clamp(_settings.WindowOpacity * 255, 60, 255), LWA_ALPHA);
        }
        else
        {
            RootGrid.Background = null; // 交给材质
            uint ex = (uint)GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
            if ((ex & WS_EX_LAYERED) != 0)
            {
                _ = SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, (IntPtr)(ulong)(ex & ~WS_EX_LAYERED));
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED));
            }
        }
        RootGrid.Opacity = 1.0; // 透明度走窗口/材质层，文字始终清晰（3.16.1 原则）
        var (r, g, b) = _settings.AccentRgb;
        var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        AccentDot.Fill = accent;
        UpdateTabStyles();
        ApplyClickThrough(_settings.ClickThrough);
        UpdateInputRegions(); // 锁定位置切换时同步拖拽区
        _sched.SetInterval(_settings.RefreshMinutes);
    }

    // ---------- 数据绑定 ----------

    private void BindSchedule(WeekSchedule? cache = null)
    {
        _view = _sched.Current ?? cache ?? _view;
        var s = _view;
        if (s == null) { UpdateStatus(); return; }
        SubtitleText.Text = $"{DateTime.Now:M月d日} {ScheduleService.WeekdayNames[ScheduleService.TodayIndex()]}"
            + $" · 今日 {s.Days.ElementAtOrDefault(ScheduleService.TodayIndex())?.Entries.Count ?? 0} 部更新";
        SourceText.Text = $"更新于 {s.FetchedAt} · {new Uri(s.Base).Host}";
        RefreshRows();
        UpdateStatus();
    }

    private void RefreshRows()
    {
        _rows.Clear();
        var day = _view?.Days.ElementAtOrDefault(_selectedDay);
        if (_view == null || day == null) return;
        var s = _view;
        var isToday = _selectedDay == ScheduleService.TodayIndex();
        var now = DateTime.Now.TimeOfDay;
        foreach (var e in day.Entries)
        {
            if (_settings.FavoritesOnly && !_settings.Favorites.Contains(e.DetailId)) continue;
            if (_search.Length > 0 && !e.Title.Contains(_search, StringComparison.OrdinalIgnoreCase)) continue;
            var isPast = isToday && TimeSpan.TryParseExact(e.Time, "hh\\:mm", null, out var t) && t < now;
            _rows.Add(new RowModel
            {
                Title = e.Title,
                Meta = e.IsEnd ? "完结" : $"{e.Time ?? "--:--"} {e.Label}".Trim(),
                DetailId = e.DetailId,
                Star = _settings.Favorites.Contains(e.DetailId) ? "★" : "☆",
                IsEnd = e.IsEnd,
                IsNewVis = e.IsNew && !isPast ? Visibility.Visible : Visibility.Collapsed,
                Url = _settings.ClickTarget == ClickTarget.Play ? e.PlayUrl(s.Base)
                    : _settings.ClickTarget == ClickTarget.Search ? e.SearchUrl(s.Base)
                    : e.DetailUrl(s.Base),
            });
        }
    }

    private void UpdateStatus()
    {
        var count = _rows.Count;
        StatusText.Text = _sched.LastError != null && _sched.Current == null
            ? $"离线 · {_sched.ProxyDesc}"
            : $"{ScheduleService.WeekdayNames[_selectedDay]} {count} 部";
    }

    private void UpdateTabStyles()
    {
        var (r, g, b) = _settings.AccentRgb;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool sel = i == _selectedDay;
            _tabButtons[i].Background = sel
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b))
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            _tabButtons[i].Foreground = sel
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
    }

    // ---------- 交互 ----------

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int idx })
        {
            _selectedDay = idx;
            UpdateTabStyles();
            RefreshRows();
            UpdateStatus();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        RefreshRows();
        UpdateStatus();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _sched.RefreshNow();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWin != null) { _settingsWin.Activate(); return; }
        _settingsWin = new SettingsWindow(_settings, this);
        _settingsWin.Closed += (_, _) => _settingsWin = null;
        _settingsWin.Activate();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => ToggleVisibility();

    public void ToggleVisibility()
    {
        _visible = !_visible;
        if (_visible) AppWindow.Show(); else AppWindow.Hide();
    }

    private void Tray_Toggle(object sender, RoutedEventArgs e) => ToggleVisibility();

    private void Tray_Exit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        Close();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            if (_settings.Favorites.Contains(id)) _settings.Favorites.Remove(id);
            else _settings.Favorites.Add(id);
            _settings.Save();
            RefreshRows();
        }
    }

    private async void Entry_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RowModel row)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(row.Url));
    }

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

            // 八向缩放边/角（6px 边，14px 角）
            const int edge = 6, corner = 14;
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.LeftBorder, new[] { new RectInt32(0, corner, edge, h - corner * 2) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.RightBorder, new[] { new RectInt32(w - edge, corner, edge, h - corner * 2) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.TopBorder, new[] { new RectInt32(corner, 0, w - corner * 2, edge) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.BottomBorder, new[] { new RectInt32(corner, h - edge, w - corner * 2, edge) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.TopLeftBorder, new[] { new RectInt32(0, 0, corner, corner) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.TopRightBorder, new[] { new RectInt32(w - corner, 0, corner, corner) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.BottomLeftBorder, new[] { new RectInt32(0, h - corner, corner, corner) });
            _ncpSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.BottomRightBorder, new[] { new RectInt32(w - corner, h - corner, corner, corner) });
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

    // ---------- 贴边磁吸 ----------

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _snapTimer;
    private bool _snapping;

    private void SnapToEdges()
    {
        if (_edgeHidden) return;
        try
        {
            var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var pos = AppWindow.Position; var size = AppWindow.Size;
            const int snap = 16;
            int nx = pos.X, ny = pos.Y;
            if (Math.Abs(pos.X - wa.X) < snap) nx = wa.X;
            else if (Math.Abs(pos.X + size.Width - (wa.X + wa.Width)) < snap) nx = wa.X + wa.Width - size.Width;
            if (Math.Abs(pos.Y - wa.Y) < snap) ny = wa.Y;
            else if (Math.Abs(pos.Y + size.Height - (wa.Y + wa.Height)) < snap) ny = wa.Y + wa.Height - size.Height;
            if (nx != pos.X || ny != pos.Y)
            {
                _snapping = true;
                AppWindow.Move(new PointInt32(nx, ny));
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

            // 悬停显示：平时全透，光标进入区域浮现（轮询实现，窗口被遮挡也能探到）
            if (_settings.HoverReveal)
            {
                byte target = inside ? CurrentNormalAlpha() : (byte)0;
                if (inside == _hoverHidden) // 状态需要翻转
                {
                    SetExStyle(WS_EX_LAYERED, true);
                    SetLayeredWindowAttributes(_hwnd, 0, target, LWA_ALPHA);
                    _hoverHidden = !inside;
                }
            }
            else if (_hoverHidden)
            {
                SetLayeredWindowAttributes(_hwnd, 0, CurrentNormalAlpha(), LWA_ALPHA);
                _hoverHidden = false;
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

    private void SetExStyle(uint flag, bool on)
    {
        uint style = (uint)GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
        ulong ns = on ? (style | flag) : (style & ~flag);
        _ = SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, (IntPtr)ns);
    }

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const uint WS_EX_TRANSPARENT = 0x20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;
    private const uint WS_DLGFRAME = 0x00400000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_BORDER = 0x00800000;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
}
