using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using SD = System.Drawing;

namespace AnimeWidget;

public partial class MainWindow : Window
{
    private readonly AppViewModel _vm = new();
    private readonly AppSettings _settings;
    private readonly ScheduleService _sched = new();
    private TaskbarIcon? _tray;
    private DesktopLayer? _layer;
    private IntPtr _hwnd;
    private SettingsWindow? _settingsWin;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        DataContext = _vm;

        // 还原窗口状态
        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            Left = _settings.Left.Value;
            Top = _settings.Top.Value;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24;
            Top = wa.Top + 80;
        }
        Opacity = _settings.WindowOpacity;

        ApplyAccent();
        ApplyBgDarkness();

        // 数据
        _sched.ScheduleUpdated += s => Dispatcher.Invoke(() =>
        {
            _vm.ApplySchedule(s);
            UpdateStatus();
        });
        _sched.StateChanged += () => Dispatcher.Invoke(() =>
        {
            if (_sched.LastError != null)
            {
                _vm.ApplyError(_sched.LastError);
                ErrorDetail.Text = _sched.LastError + $"\n（当前{_sched.ProxyDesc}）";
            }
            else
            {
                _vm.ClearError();
            }
            ErrorBanner.Visibility = _vm.HasError ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        });
        _sched.Start(_settings.RefreshMinutes);

        _vm.DayChanged += () =>
        {
            UpdateTabStyles();
            UpdateStatus();
        };
        BuildTabs();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.EnableAcrylic(_hwnd);
        _layer = new DesktopLayer(_hwnd, _settings.EmbedMode);
        ApplyClickThrough();
        SetupTray();
    }

    // ---------- 外观 ----------

    private void ApplyAccent()
    {
        var (r, g, b) = _settings.AccentRgb;
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        _vm.AccentBrush = brush;
        AccentDot.Fill = brush;
        UpdateTabStyles();
    }

    private void ApplyBgDarkness()
    {
        // bg_darkness 0(最浅)~1(最深)：控制根背景不透明度与深浅
        var d = Math.Clamp(_settings.BgDarkness, 0, 1);
        var v = (byte)(28 - d * 14);              // 28(亮灰黑) → 14(近黑)
        var a = (byte)(0xD8 + d * 0x20);          // 越深越实
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(a, v, (byte)(v + 2), (byte)(v + 8)));
    }

    // ---------- 周几 tabs（代码构建，accent 感知） ----------

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var idx = i;
            var isToday = i == _vm.TodayIndex;
            var border = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 3.5, 8, 3.5),
                Margin = new Thickness(0, 0, 3, 0),
                Cursor = Cursors.Hand,
                Tag = idx,
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var txt = new TextBlock { Text = ScheduleService.WeekdayNames[i], FontSize = 12 };
            sp.Children.Add(txt);
            if (isToday)
            {
                var dot = new TextBlock
                {
                    Text = " •",
                    FontSize = 12,
                    Foreground = _vm.AccentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                sp.Children.Add(dot);
            }
            border.Child = sp;
            border.MouseLeftButtonUp += (_, _) =>
            {
                _vm.SelectedDay = idx;
                UpdateTabStyles();
            };
            border.MouseEnter += (_, _) =>
            {
                if (_vm.SelectedDay != idx)
                    border.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
            };
            border.MouseLeave += (_, _) => UpdateTabStyles();
            TabPanel.Children.Add(border);
        }
        UpdateTabStyles();
    }

    private void UpdateTabStyles()
    {
        var accent = _vm.AccentBrush.Color;
        foreach (var child in TabPanel.Children.OfType<Border>())
        {
            var idx = (int)child.Tag;
            var sp = (StackPanel)child.Child;
            var txt = (TextBlock)sp.Children[0];
            if (idx == _vm.SelectedDay)
            {
                child.Background = new SolidColorBrush(
                    Color.FromArgb(0x38, accent.R, accent.G, accent.B));
                txt.Foreground = _vm.AccentBrush;
                txt.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                child.Background = Brushes.Transparent;
                txt.Foreground = idx == _vm.TodayIndex
                    ? new SolidColorBrush(Color.FromRgb(0xD0, 0xD3, 0xDC))
                    : new SolidColorBrush(Color.FromRgb(0x8F, 0x93, 0xA3));
                txt.FontWeight = FontWeights.Normal;
            }
        }
    }

    // ---------- 条目点击 ----------

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EntryViewModel entry)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    entry.Url(_settings.ClickTarget)) { UseShellExecute = true });
            }
            catch { }
        }
    }

    // ---------- 托盘 ----------

    private void SetupTray()
    {
        var menu = new ContextMenu();

        var miShow = new MenuItem { Header = "显示 / 隐藏" };
        miShow.Click += (_, _) => ToggleVisibility();

        var miThrough = new MenuItem { Header = "鼠标穿透", IsCheckable = true, IsChecked = _settings.ClickThrough };
        miThrough.Click += (_, _) =>
        {
            _settings.ClickThrough = miThrough.IsChecked;
            ApplyClickThrough();
            _settings.Save();
        };

        var miLock = new MenuItem { Header = "锁定位置", IsCheckable = true, IsChecked = _settings.Locked };
        miLock.Click += (_, _) =>
        {
            _settings.Locked = miLock.IsChecked;
            _settings.Save();
        };

        var miAutostart = new MenuItem { Header = "开机自启", IsCheckable = true, IsChecked = GetAutostart() };
        miAutostart.Click += (_, _) => SetAutostart(miAutostart.IsChecked);

        var miOpacity = new MenuItem { Header = "透明度" };
        foreach (var pct in new[] { 60, 70, 80, 90, 100 })
        {
            var item = new MenuItem
            {
                Header = $"{pct}%",
                IsCheckable = true,
                IsChecked = Math.Abs(_settings.WindowOpacity * 100 - pct) < 1,
            };
            var v = pct / 100.0;
            item.Click += (_, _) =>
            {
                Opacity = v;
                _settings.WindowOpacity = v;
                foreach (var sib in miOpacity.Items.OfType<MenuItem>()) sib.IsChecked = ReferenceEquals(sib, item);
                _settings.Save();
            };
            miOpacity.Items.Add(item);
        }

        var miRefresh = new MenuItem { Header = "立即刷新" };
        miRefresh.Click += (_, _) => _sched.RefreshNow();

        var miSettings = new MenuItem { Header = "设置…" };
        miSettings.Click += (_, _) => OpenSettings();

        var miQuit = new MenuItem { Header = "退出" };
        miQuit.Click += (_, _) => App.Current.Shutdown();

        menu.Items.Add(miShow);
        menu.Items.Add(miThrough);
        menu.Items.Add(miLock);
        menu.Items.Add(miAutostart);
        menu.Items.Add(miOpacity);
        menu.Items.Add(new Separator());
        menu.Items.Add(miSettings);
        menu.Items.Add(miRefresh);
        menu.Items.Add(new Separator());
        menu.Items.Add(miQuit);

        _tray = new TaskbarIcon
        {
            Icon = MakeIcon(),
            ToolTipText = "追番周表",
            ContextMenu = menu,
        };
        _tray.TrayMouseDoubleClick += (_, _) => ToggleVisibility();
    }

    private void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible) Hide();
        else { Show(); Activate(); }
    }

    // ---------- 开机自启 ----------

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AnimeFollowingWidget";

    private static bool GetAutostart()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch { return false; }
    }

    private static void SetAutostart(bool on)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            if (on)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    private static SD.Icon MakeIcon()
    {
        var bmp = new SD.Bitmap(64, 64);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SD.SolidBrush(SD.Color.FromArgb(45, 160, 190, 200));
            using var path = RoundedRect(new SD.Rectangle(4, 4, 56, 56), 14);
            g.FillPath(bg, path);
            using var font = new SD.Font("Microsoft YaHei UI", 30, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var white = new SD.SolidBrush(SD.Color.White);
            var fmt = new SD.StringFormat { Alignment = SD.StringAlignment.Center, LineAlignment = SD.StringAlignment.Center };
            g.DrawString("番", font, white, new SD.RectangleF(0, 2, 64, 64), fmt);
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }

    private static SD.Drawing2D.GraphicsPath RoundedRect(SD.Rectangle r, int radius)
    {
        var p = new SD.Drawing2D.GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- 设置窗口 ----------

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWin != null)
        {
            _settingsWin.Activate();
            return;
        }
        // 打开设置时临时关掉穿透，否则设置窗也点不到
        if (_settings.ClickThrough) Win32.SetClickThrough(_hwnd, false);
        _settingsWin = new SettingsWindow(_settings, _sched)
        {
            Owner = this,
        };
        _settingsWin.AppearanceChanged += () =>
        {
            Opacity = _settings.WindowOpacity;
            ApplyAccent();
            ApplyBgDarkness();
        };
        _settingsWin.EmbedModeChanged += mode => _layer?.SetMode(mode);
        _settingsWin.Closed += (_, _) =>
        {
            _settingsWin = null;
            ApplyClickThrough();
            _settings.Save();
        };
        _settingsWin.Show();
    }

    // ---------- 窗口行为 ----------

    private void Root_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is Button) return;
        if (_settings.Locked) return;
        try { DragMove(); } catch { }
        SnapToEdges();
        Persist();
    }

    private const double SnapDist = 20;
    private const double EdgeMargin = 8;

    private void SnapToEdges()
    {
        var wa = SystemParameters.WorkArea;
        if (Math.Abs(Left - wa.Left) < SnapDist) Left = wa.Left + EdgeMargin;
        else if (Math.Abs(wa.Right - (Left + Width)) < SnapDist) Left = wa.Right - Width - EdgeMargin;
        if (Math.Abs(Top - wa.Top) < SnapDist) Top = wa.Top + EdgeMargin;
        else if (Math.Abs(wa.Bottom - (Top + Height)) < SnapDist) Top = wa.Bottom - Height - EdgeMargin;
    }

    private void Grip_Resize(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _hwnd != IntPtr.Zero)
        {
            Win32.BeginNativeResize(_hwnd);
            Persist();
        }
    }

    private void ApplyClickThrough()
    {
        if (_hwnd != IntPtr.Zero)
            Win32.SetClickThrough(_hwnd, _settings.ClickThrough);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _sched.RefreshNow();

    private void ErrorBanner_Click(object sender, MouseButtonEventArgs e) => _sched.RefreshNow();

    private void UpdateStatus()
    {
        var count = _vm.SelectedDayCount;
        StatusText.Text = _sched.LastError != null && _sched.Current == null
            ? $"离线 · 代理：{_sched.ProxyDesc}"
            : $"{ScheduleService.WeekdayNames[_vm.SelectedDay]} {count} 部 · {_sched.ProxyDesc}";
    }

    private void Persist()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Persist();
        _tray?.Dispose();
        _sched.Dispose();
        base.OnClosing(e);
    }
}

/// <summary>bool → Visibility，参数 invert 取反。</summary>
public sealed class BoolVisConverter : IValueConverter
{
    public static readonly BoolVisConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
