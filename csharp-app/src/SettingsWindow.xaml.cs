using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AnimeWidget;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ScheduleService _sched;
    private bool _ready;

    // 强调色画刷：唯一实例 + 换色只改 .Color，所有引用处（代码构建的胶囊/圆环/卡片描边、
    // XAML 里 StaticResource 已解析的同一对象）全部实时联动 —— 设置页跟随主题色，不再焊死青色。
    private readonly SolidColorBrush AccentBrush;
    private static readonly Brush PillOffBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush PillTextOff = new SolidColorBrush(Color.FromRgb(0xDD, 0xE1, 0xEA));

    /// <summary>透明度/强调色/背景深浅/磨砂变了（MainWindow 重刷外观）。</summary>
    public event Action? AppearanceChanged;
    public event Action<EmbedMode>? EmbedModeChanged;

    /// <summary>「只显示收藏」等影响列表的设置变更（MainWindow 订阅以刷新列表）。</summary>
    public event Action? ListRefreshNeeded;

    public SettingsWindow(AppSettings settings, ScheduleService sched)
    {
        InitializeComponent();
        _settings = settings;
        _sched = sched;

        // Win11 22621+：DWM 原生圆角 + Acrylic 系统背景板（不碰 AllowsTransparency，保住 ClearType）。
        // Win10/旧 DWM 调用失败 → 不动 Background，保持 XAML 里的 #FF161A22 纯色兜底，绝不出现透明破图。
        SourceInitialized += (_, _) =>
        {
            if (Environment.OSVersion.Version.Build >= 22621 &&
                Win32.ApplyModernChrome(new WindowInteropHelper(this).Handle))
            {
                Background = Brushes.Transparent; // 系统背景板顶到最底层，纯色背景让位
            }
        };

        // 用当前主题色初始化唯一画刷实例，并覆盖 XAML 默认资源（同一对象，后续改 .Color 即全局联动）
        var (_, ar, ag, ab) = AppSettings.Accents[Math.Clamp(settings.Accent, 0, AppSettings.Accents.Length - 1)];
        AccentBrush = new SolidColorBrush(Color.FromRgb(ar, ag, ab));
        Resources["AccentBrush"] = AccentBrush;

        // 外观
        OpacitySlider.Value = settings.WindowOpacity;
        DarknessSlider.Value = settings.BgDarkness;
        BuildAccentSwatches();

        // 行为：分段胶囊（替代单选/下拉，所见即所得）
        BuildPills(ClickPills, new[] { "详情页", "播放页", "搜索页" },
            settings.ClickTarget switch { ClickTarget.Detail => 0, ClickTarget.Play => 1, _ => 2 },
            i => SetClick(i switch { 0 => ClickTarget.Detail, 1 => ClickTarget.Play, _ => ClickTarget.Search }));
        var mins = new[] { 15, 30, 60, 120 };
        var mi = Array.IndexOf(mins, settings.RefreshMinutes);
        BuildPills(RefreshPills, mins.Select(m => $"{m} 分钟").ToArray(),
            mi >= 0 ? mi : 1,
            i => { _settings.RefreshMinutes = mins[i]; _sched.SetInterval(mins[i]); _settings.Save(); });

        ThroughCheck.IsChecked = settings.ClickThrough;
        ThroughCheck.Checked += (_, _) => { settings.ClickThrough = true; settings.Save(); };
        ThroughCheck.Unchecked += (_, _) => { settings.ClickThrough = false; settings.Save(); };

        LockCheck.IsChecked = settings.Locked;
        LockCheck.Checked += (_, _) => { settings.Locked = true; settings.Save(); };
        LockCheck.Unchecked += (_, _) => { settings.Locked = false; settings.Save(); };

        NotifyCheck.IsChecked = settings.NotifyOnAir;
        NotifyCheck.Checked += (_, _) => { settings.NotifyOnAir = true; settings.Save(); };
        NotifyCheck.Unchecked += (_, _) => { settings.NotifyOnAir = false; settings.Save(); };

        FavOnlyCheck.IsChecked = settings.FavoritesOnly;
        FavOnlyCheck.Checked += (_, _) => { settings.FavoritesOnly = true; settings.Save(); ListRefreshNeeded?.Invoke(); };
        FavOnlyCheck.Unchecked += (_, _) => { settings.FavoritesOnly = false; settings.Save(); ListRefreshNeeded?.Invoke(); };

        AutoSinkCheck.IsChecked = settings.AutoSink;
        AutoSinkCheck.Checked += (_, _) => { settings.AutoSink = true; settings.Save(); };
        AutoSinkCheck.Unchecked += (_, _) => { settings.AutoSink = false; settings.Save(); };

        EdgeHideCheck.IsChecked = settings.EdgeHide;
        EdgeHideCheck.Checked += (_, _) => { settings.EdgeHide = true; settings.Save(); };
        EdgeHideCheck.Unchecked += (_, _) => { settings.EdgeHide = false; settings.Save(); };

        HoverRevealCheck.IsChecked = settings.HoverReveal;
        HoverRevealCheck.Checked += (_, _) => { settings.HoverReveal = true; settings.Save(); };
        HoverRevealCheck.Unchecked += (_, _) => { settings.HoverReveal = false; settings.Save(); };

        // 嵌入选项卡高亮
        UpdateEmbedCards();

        // 嵌入选项卡 hover 反馈（优效同款：悬停微亮，移开还原）
        foreach (var card in new[] { EmbedCardNormal, EmbedCardWorkerW, EmbedCardBottomPin })
        {
            card.MouseEnter += EmbedCard_Hover;
            card.MouseLeave += EmbedCard_Unhover;
        }

        // 数据
        ProxyText.Text = $"代理：{sched.ProxyDesc}";
        SourceTextDiag.Text = sched.Current != null
            ? $"数据源：{new Uri(sched.Current.Base).Host} · 更新于 {sched.Current.FetchedAt}"
            : "数据源：尚未成功（显示缓存/无数据）";
        LastErrorText.Text = sched.LastError != null ? $"最近错误：\n{sched.LastError}" : "";

        _ready = true;
        OpacityValue.Text = $"{_settings.WindowOpacity:P0}";
        DarknessValue.Text = $"{_settings.BgDarkness:P0}";
    }

    // ---------- 强调色圆点（选中带描边环） ----------

    private void BuildAccentSwatches()
    {
        AccentPanel.Children.Clear();
        for (var i = 0; i < AppSettings.Accents.Length; i++)
        {
            var idx = i;
            var (_, r, g, b) = AppSettings.Accents[i];
            var ring = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                BorderBrush = _settings.Accent == idx ? AccentBrush : Brushes.Transparent,
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                ToolTip = AppSettings.Accents[idx].Name,
            };
            ring.Child = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
            };
            ring.MouseLeftButtonUp += (_, _) =>
            {
                _settings.Accent = idx;
                _settings.Save();
                AccentBrush.Color = Color.FromRgb(r, g, b); // 单实例换色，全设置页实时跟随
                BuildAccentSwatches(); // 重排选中环
                AppearanceChanged?.Invoke();
            };
            AccentPanel.Children.Add(ring);
        }
    }

    // ---------- 分段胶囊 ----------

    private void BuildPills(Panel host, string[] labels, int selected, Action<int> onPick)
    {
        host.Children.Clear();
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Background = idx == selected ? AccentBrush : PillOffBrush,
                Child = new TextBlock
                {
                    Text = labels[idx],
                    FontSize = 11.5,
                    Foreground = idx == selected ? Brushes.White : PillTextOff,
                },
            };
            b.MouseLeftButtonUp += (_, _) =>
            {
                onPick(idx);
                BuildPills(host, labels, idx, onPick); // 重排高亮
            };
            host.Children.Add(b);
        }
    }

    // ---------- 嵌入选项卡 ----------

    private static readonly Brush CardRestBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CardHoverBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    private void EmbedCard_Hover(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = CardHoverBrush;
    }

    private void EmbedCard_Unhover(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = CardRestBrush;
    }

    private void EmbedCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_ready || sender is not Border card) return;
        SetEmbed(int.Parse((string)card.Tag) switch { 0 => EmbedMode.Normal, 1 => EmbedMode.WorkerW, _ => EmbedMode.BottomPin });
        UpdateEmbedCards();
    }

    private void UpdateEmbedCards()
    {
        var sel = _settings.EmbedMode;
        foreach (var (card, mode) in new[]
        {
            (EmbedCardNormal, EmbedMode.Normal),
            (EmbedCardWorkerW, EmbedMode.WorkerW),
            (EmbedCardBottomPin, EmbedMode.BottomPin),
        })
        {
            var on = sel == mode;
            card.BorderThickness = new Thickness(1.5);
            card.BorderBrush = on ? AccentBrush : Brushes.Transparent;
        }
    }

    // ---------- 滑杆（实时应用 + 持久化——v3.9.3 漏了 Save，重启丢设置） ----------

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.WindowOpacity = Math.Round(e.NewValue, 2);
        OpacityValue.Text = $"{_settings.WindowOpacity:P0}";
        _settings.Save();
        AppearanceChanged?.Invoke();
    }

    private void Darkness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.BgDarkness = Math.Round(e.NewValue, 2);
        DarknessValue.Text = $"{_settings.BgDarkness:P0}";
        _settings.Save();
        AppearanceChanged?.Invoke();
    }

    private void SetClick(ClickTarget t)
    {
        if (!_ready) return;
        _settings.ClickTarget = t;
        _settings.Save();
    }

    private void SetEmbed(EmbedMode m)
    {
        if (!_ready) return;
        _settings.EmbedMode = m;
        _settings.Save();
        EmbedModeChanged?.Invoke(m);
    }

    private void RefreshNow_Click(object sender, RoutedEventArgs e) => _sched.RefreshNow();

    // ---------- 自定义标题栏 ----------

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
