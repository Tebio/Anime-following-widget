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

        // ---- 材质：Win10 Acrylic / Win11 Mica，手动给深色 Tint（默认浅色调=白窗根因） ----
        try
        {
            if (Environment.OSVersion.Version.Build >= 22000 && MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            else if (DesktopAcrylicController.IsSupported())
                SystemBackdrop = new DesktopAcrylicBackdrop
                {
                    TintColor = Windows.UI.Color.FromArgb(255, 0x1F, 0x28, 0x38),
                    TintOpacity = 0.55f,
                    LuminosityOpacity = 0.25f,
                    FallbackColor = Windows.UI.Color.FromArgb(255, 0x1F, 0x28, 0x38),
                };
            BootLog.Log("Backdrop ok");
        }
        catch (Exception ex) { BootLog.Log("Backdrop fail: " + ex.Message); }

        // ---- 全窗口拖拽：根 Grid 为拖拽区；SetBorderAndTitleBar(false,false) 彻底卸系统框 ----
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(RootGrid);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false); // 否则失焦时右上角浮出原生 X（"两个X"根因）
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        AppWindow.Resize(new SizeInt32(360, 540));
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
        RootGrid.Opacity = Math.Clamp(_settings.WindowOpacity, 0.2, 1.0);
        var (r, g, b) = _settings.AccentRgb;
        var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        AccentDot.Fill = accent;
        UpdateTabStyles();
        ApplyClickThrough(_settings.ClickThrough);
        ApplyLock(_settings.Locked);
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

    // ---------- Win32：鼠标穿透 / 锁定位置 ----------

    private void ApplyClickThrough(bool on) => SetExStyle(WS_EX_TRANSPARENT, on);

    public void ApplyLock(bool locked) => SetTitleBar(locked ? null : RootGrid);

    private void SetExStyle(uint flag, bool on)
    {
        uint style = (uint)GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
        ulong ns = on ? (style | flag) : (style & ~flag);
        _ = SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, (IntPtr)ns);
    }

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
