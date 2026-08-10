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
        // 任务栏不显示（托盘已够）：WS_EX_TOOLWINDOW；Alt+Tab 也藏掉
        AppWindow.IsShownInSwitchers = false;
        SetExStyle(WS_EX_TOOLWINDOW, true);
        AppWindow.Resize(new SizeInt32(360, 540));

        // ---- 原生级拖拽+八向缩放：NonClientPointerSource 区域（无标题栏窗口的正解） ----
        _ncpSource = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        RootGrid.SizeChanged += (_, _) => { UpdateInputRegions(); ApplyRoundedRegion(); };
        TabPanel.Loaded += (_, _) => UpdateInputRegions();

        // ---- 贴边磁吸由轮询驱动（见 SnapTick），不再依赖 AppWindow.Changed ----

        // ---- 悬停显示/贴边隐藏：光标轮询（不依赖窗口消息，被遮挡也能探到） ----
        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(150);
        _pollTimer.Tick += (_, _) => { PollCursor(); SnapTick(); };
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
        SetupTray(); // 菜单+图标+左键命令全部在代码里构建（DeskBox 方式）
        Closed += (_, _) => _sched.Dispose();
        _current = this;
        StartWatchdog();
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
        _hoverAlpha = 255; // 材质/透明度切换后复位悬停 alpha 状态机
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

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "刷新中…";
        _sched.RefreshNow();
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
}
