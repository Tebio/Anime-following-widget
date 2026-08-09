using AnimeWidget;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AnimeWidget.WinUI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly WidgetWindow _owner;
    private bool _ready;

    private static readonly (string Name, ClickTarget T)[] ClickTargets =
        { ("详情页", ClickTarget.Detail), ("播放页", ClickTarget.Play), ("搜索页", ClickTarget.Search) };
    private static readonly int[] Intervals = { 15, 30, 60, 120 };

    public SettingsWindow(AppSettings settings, WidgetWindow owner)
    {
        InitializeComponent();
        _settings = settings;
        _owner = owner;

        Root.RequestedTheme = ElementTheme.Dark;
        BackdropHelper.ApplyDarkAcrylic(this);

        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, true); // 设置窗保留正常标题栏
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        AppWindow.Resize(new SizeInt32(380, 620));
        Title = "追番周表 · 设置";

        // 透明度
        OpacitySlider.Value = Math.Round(_settings.WindowOpacity * 100);
        OpacityLabel.Text = $"窗口透明度 {OpacitySlider.Value:0}%（亚克力背景下调低更透）";

        // 强调色
        for (int i = 0; i < AppSettings.Accents.Length; i++)
        {
            var (name, r, g, b) = AppSettings.Accents[i];
            var border = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = i,
            };
            ToolTipService.SetToolTip(border, name);
            if (i == _settings.Accent) border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
            border.Tapped += Accent_Tapped;
            AccentPanel.Children.Add(border);
        }

        // 点击目标
        foreach (var (name, t) in ClickTargets)
        {
            var b = MakePill(name, _settings.ClickTarget == t);
            b.Tag = t;
            b.Click += ClickTarget_Click;
            ClickTargetPanel.Children.Add(b);
        }

        // 刷新间隔
        foreach (var m in Intervals)
        {
            var b = MakePill($"{m}分钟", _settings.RefreshMinutes == m);
            b.Tag = m;
            b.Click += Interval_Click;
            IntervalPanel.Children.Add(b);
        }

        ClickThroughSwitch.IsOn = _settings.ClickThrough;
        LockSwitch.IsOn = _settings.Locked;
        NotifySwitch.IsOn = _settings.NotifyOnAir;
        FavOnlySwitch.IsOn = _settings.FavoritesOnly;
        _ready = true;
    }

    private Button MakePill(string text, bool selected)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 11.5,
        };
        StylePill(b, selected);
        return b;
    }

    private void StylePill(Button b, bool selected)
    {
        var (r, g, bl) = _settings.AccentRgb;
        b.Background = selected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, bl))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        b.Foreground = selected
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void RestylePanel(Panel panel, object selectedTag)
    {
        foreach (var child in panel.Children)
            if (child is Button b) StylePill(b, Equals(b.Tag, selectedTag));
    }

    private void Save() { _settings.Save(); _owner.ApplySettings(); }

    private void Accent_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: int idx })
        {
            _settings.Accent = idx;
            for (int i = 0; i < AccentPanel.Children.Count; i++)
                if (AccentPanel.Children[i] is Border bd)
                    bd.BorderBrush = new SolidColorBrush(i == idx ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Transparent);
            RestylePanel(ClickTargetPanel, _settings.ClickTarget);
            RestylePanel(IntervalPanel, _settings.RefreshMinutes);
            Save();
        }
    }

    private void ClickTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClickTarget t })
        {
            _settings.ClickTarget = t;
            RestylePanel(ClickTargetPanel, t);
            Save();
        }
    }

    private void Interval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int m })
        {
            _settings.RefreshMinutes = m;
            RestylePanel(IntervalPanel, m);
            Save();
        }
    }

    private void Opacity_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.WindowOpacity = e.NewValue / 100.0;
        OpacityLabel.Text = $"窗口透明度 {e.NewValue:0}%（亚克力背景下调低更透）";
        Save();
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.ClickThrough = ClickThroughSwitch.IsOn;
        _settings.Locked = LockSwitch.IsOn;
        _settings.NotifyOnAir = NotifySwitch.IsOn;
        _settings.FavoritesOnly = FavOnlySwitch.IsOn;
        Save();
    }
}
