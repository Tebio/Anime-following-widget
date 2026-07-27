using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeWidget;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ScheduleService _sched;
    private bool _ready;

    /// <summary>透明度/强调色/背景深浅变了（MainWindow 重刷外观）。</summary>
    public event Action? AppearanceChanged;
    public event Action<EmbedMode>? EmbedModeChanged;

    public SettingsWindow(AppSettings settings, ScheduleService sched)
    {
        InitializeComponent();
        _settings = settings;
        _sched = sched;

        // 外观
        OpacitySlider.Value = settings.WindowOpacity;
        DarknessSlider.Value = settings.BgDarkness;
        BuildAccentRadios();

        // 行为
        ClickDetail.IsChecked = settings.ClickTarget == ClickTarget.Detail;
        ClickSearch.IsChecked = settings.ClickTarget == ClickTarget.Search;
        ClickDetail.Checked += (_, _) => SetClick(ClickTarget.Detail);
        ClickSearch.Checked += (_, _) => SetClick(ClickTarget.Search);

        foreach (var m in new[] { 15, 30, 60, 120 })
        {
            var item = new ComboBoxItem { Content = $"{m} 分钟", Tag = m };
            RefreshCombo.Items.Add(item);
            if (m == settings.RefreshMinutes) RefreshCombo.SelectedItem = item;
        }
        if (RefreshCombo.SelectedItem == null) RefreshCombo.SelectedIndex = 1;

        ThroughCheck.IsChecked = settings.ClickThrough;
        ThroughCheck.Checked += (_, _) => { settings.ClickThrough = true; settings.Save(); };
        ThroughCheck.Unchecked += (_, _) => { settings.ClickThrough = false; settings.Save(); };

        LockCheck.IsChecked = settings.Locked;
        LockCheck.Checked += (_, _) => { settings.Locked = true; settings.Save(); };
        LockCheck.Unchecked += (_, _) => { settings.Locked = false; settings.Save(); };

        NotifyCheck.IsChecked = settings.NotifyOnAir;
        NotifyCheck.Checked += (_, _) => { settings.NotifyOnAir = true; settings.Save(); };
        NotifyCheck.Unchecked += (_, _) => { settings.NotifyOnAir = false; settings.Save(); };

        // 嵌入
        EmbedNormal.IsChecked = settings.EmbedMode == EmbedMode.Normal;
        EmbedWorkerW.IsChecked = settings.EmbedMode == EmbedMode.WorkerW;
        EmbedBottomPin.IsChecked = settings.EmbedMode == EmbedMode.BottomPin;
        EmbedNormal.Checked += (_, _) => SetEmbed(EmbedMode.Normal);
        EmbedWorkerW.Checked += (_, _) => SetEmbed(EmbedMode.WorkerW);
        EmbedBottomPin.Checked += (_, _) => SetEmbed(EmbedMode.BottomPin);

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

    private void BuildAccentRadios()
    {
        for (var i = 0; i < AppSettings.Accents.Length; i++)
        {
            var idx = i;
            var (name, r, g, b) = AppSettings.Accents[i];
            var rb = new RadioButton { GroupName = "accent", IsChecked = _settings.Accent == idx };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 12, Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            rb.Content = sp;
            rb.Checked += (_, _) =>
            {
                _settings.Accent = idx;
                _settings.Save();
                AppearanceChanged?.Invoke();
            };
            AccentPanel.Children.Add(rb);
        }
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.WindowOpacity = Math.Round(e.NewValue, 2);
        OpacityValue.Text = $"{_settings.WindowOpacity:P0}";
        AppearanceChanged?.Invoke();
    }

    private void Darkness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.BgDarkness = Math.Round(e.NewValue, 2);
        DarknessValue.Text = $"{_settings.BgDarkness:P0}";
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

    private void RefreshCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || RefreshCombo.SelectedItem is not ComboBoxItem item) return;
        _settings.RefreshMinutes = (int)item.Tag;
        _sched.SetInterval((int)item.Tag); // 立即生效，不用重启
        _settings.Save();
    }

    private void RefreshNow_Click(object sender, RoutedEventArgs e) => _sched.RefreshNow();

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
