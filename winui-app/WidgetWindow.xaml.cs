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
    private WeekSchedule? _view; // 当前展示用的周表（缓存或最新抓取）

    public WidgetWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();

        // ---- 材质：Win10 用 DesktopAcrylic，Win11 优先 Mica；都不支持退回纯色 ----
        try
        {
            if (Environment.OSVersion.Version.Build >= 22000 && MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            else if (DesktopAcrylicController.IsSupported())
                SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch { /* 无材质 → 纯色兜底 */ }

        // ---- 无边框 + 拖拽区 + 尺寸/初始位置（DisplayArea 原生多屏感知） ----
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragArea);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
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

        // ---- 周几 tab ----
        _selectedDay = ScheduleService.TodayIndex();
        for (int i = 0; i < 7; i++)
        {
            var b = new Button
            {
                Content = ScheduleService.WeekdayNames[i],
                Padding = new Thickness(9, 3),
                FontSize = 11,
                Tag = i,
            };
            b.Click += Tab_Click;
            _tabButtons.Add(b);
            TabPanel.Children.Add(b);
        }
        UpdateTabStyles();

        // ---- 数据 ----
        _sched.ScheduleUpdated += _ => DispatcherQueue.TryEnqueue(() => BindSchedule());
        _sched.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateStatus);
        BindSchedule(AppSettings.LoadCache()); // 先上缓存秒开，后台再刷
        _sched.Start(_settings.RefreshMinutes);
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
                Url = e.DetailUrl(s.Base),
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
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool sel = i == _selectedDay;
            _tabButtons[i].Background = sel
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _sched.Dispose();
        Close();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            if (!_settings.Favorites.Add(id)) _settings.Favorites.Remove(id);
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
