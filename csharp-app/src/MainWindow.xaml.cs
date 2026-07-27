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
        if (_settings.Width.HasValue) Width = _settings.Width.Value;
        if (_settings.Height.HasValue) Height = _settings.Height.Value;
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

        // 分钟级计时器：跨天自动翻页 + 到点置灰/提醒
        var lastDay = DateTime.Now.Date;
        _lastAirCheck = DateTime.Now.TimeOfDay; // 启动前已播出的不补提醒
        var dayTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        dayTimer.Tick += (_, _) =>
        {
            var now = DateTime.Now.TimeOfDay;
            if (DateTime.Now.Date != lastDay)
            {
                lastDay = DateTime.Now.Date;
                _lastAirCheck = TimeSpan.Zero; // 新的一天从头检测到点
                _vm.SelectedDay = _vm.TodayIndex;
                _vm.RefreshEntries();
                UpdateTabStyles();
                UpdateStatus();
            }
            CheckAirTime(now);
        };
        dayTimer.Start();
    }

    /// <summary>上次到点检测的时刻（增量检测，避免重复提醒/开机补报）。</summary>
    private TimeSpan _lastAirCheck;

    /// <summary>到点检测：新播出的条目自动置灰，可选托盘气泡提醒。</summary>
    private void CheckAirTime(TimeSpan now)
    {
        var today = _sched.Current?.Days.ElementAtOrDefault(_vm.TodayIndex);
        if (today == null) { _lastAirCheck = now; return; }
        var crossed = today.Entries.Where(e => e.Time != null
                && TimeSpan.TryParseExact(e.Time, "hh\\:mm", null, out var t)
                && t <= now && t > _lastAirCheck).ToList();
        _lastAirCheck = now;
        if (crossed.Count == 0) return;
        _vm.RefreshEntries(); // 触发置灰重算
        if (!_settings.NotifyOnAir) return;
        var names = string.Join("、", crossed.Take(3).Select(e => e.Title));
        if (crossed.Count > 3) names += $" 等 {crossed.Count} 部";
        _tray?.ShowBalloonTip("番剧更新",
            $"{names} {crossed[0].Label} 已播出",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.None);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.EnableAcrylic(_hwnd);
        _layer = new DesktopLayer(_hwnd, _settings.EmbedMode);
        ApplyTopmost();
        ApplyClickThrough();
        SetupTray();
    }

    /// <summary>置顶只在普通窗口模式下生效（嵌入桌面层后无意义）。</summary>
    private void ApplyTopmost()
    {
        Topmost = _settings.Topmost && (_layer?.Mode ?? EmbedMode.Normal) == EmbedMode.Normal;
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
        // bg_darkness 0(通透磨砂)~1(深沉)：alpha 从 0x40 到 0x100，
        // 默认 0.6 附近时卡片半透，底层亚克力磨砂透出来
        var d = Math.Clamp(_settings.BgDarkness, 0, 1);
        var v = (byte)(28 - d * 14);              // 28(亮灰黑) → 14(近黑)
        var a = (byte)Math.Min(255, 0x40 + d * 0xC0);
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
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 4.5, 9, 4.5),
                Margin = new Thickness(0, 0, 4, 0),
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
            // 今日圆点跟随强调色（修复切换强调色后圆点保持旧色）
            if (sp.Children.Count > 1 && sp.Children[1] is TextBlock dot)
                dot.Foreground = _vm.AccentBrush;
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

    private MenuItem? _miThrough, _miLock, _miTop, _miOpacity;

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

        var miTop = new MenuItem { Header = "置顶（普通窗口模式生效）", IsCheckable = true, IsChecked = _settings.Topmost };
        miTop.Click += (_, _) =>
        {
            _settings.Topmost = miTop.IsChecked;
            ApplyTopmost();
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

        _miThrough = miThrough;
        _miLock = miLock;
        _miTop = miTop;
        _miOpacity = miOpacity;

        var miRefresh = new MenuItem { Header = "立即刷新" };
        miRefresh.Click += (_, _) => _sched.RefreshNow();

        var miSettings = new MenuItem { Header = "设置…" };
        miSettings.Click += (_, _) => OpenSettings();

        var miQuit = new MenuItem { Header = "退出" };
        miQuit.Click += (_, _) => App.Current.Shutdown();

        menu.Items.Add(miShow);
        menu.Items.Add(miThrough);
        menu.Items.Add(miLock);
        menu.Items.Add(miTop);
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

    /// <summary>托盘勾选态与设置窗双向同步（设置窗关闭时调用）。</summary>
    private void SyncTrayChecks()
    {
        if (_miThrough != null) _miThrough.IsChecked = _settings.ClickThrough;
        if (_miLock != null) _miLock.IsChecked = _settings.Locked;
        if (_miTop != null) _miTop.IsChecked = _settings.Topmost;
        if (_miOpacity != null)
            foreach (var item in _miOpacity.Items.OfType<MenuItem>())
                item.IsChecked = Math.Abs(_settings.WindowOpacity * 100
                    - int.Parse(item.Header.ToString()!.TrimEnd('%'))) < 1;
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
        _settingsWin.EmbedModeChanged += mode =>
        {
            _layer?.SetMode(mode);
            ApplyTopmost();
        };
        _settingsWin.Closed += (_, _) =>
        {
            _settingsWin = null;
            ApplyClickThrough();
            SyncTrayChecks(); // 托盘勾选态跟上设置窗改动
            _settings.Save();
        };
        _settingsWin.Show();
    }

    // ---------- 窗口行为 ----------

    // 手动拖拽：WorkerW 子窗口没有标题栏，DragMove() 会失效，必须自己算坐标
    private bool _dragging;
    private Point _dragOffset;

    private void Root_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is Button) return;
        if (_settings.Locked) return;
        _dragging = true;
        _dragOffset = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = PointToScreen(e.GetPosition(this));
        if (_resizing)
        {
            Width = Math.Max(MinWidth, _resizeW + cursor.X - _resizeOrigin.X);
            Height = Math.Max(MinHeight, _resizeH + cursor.Y - _resizeOrigin.Y);
            return;
        }
        if (!_dragging) return;
        Left = cursor.X - _dragOffset.X;
        Top = cursor.Y - _dragOffset.Y;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();
            Persist();
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
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

    // 手动缩放：WorkerW 子窗口下 WM_NCLBUTTONDOWN HTBOTTOMRIGHT 和 DragMove 一样失效
    private bool _resizing;
    private Point _resizeOrigin;
    private double _resizeW, _resizeH;

    private void Grip_Resize(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _resizing = true;
        _resizeOrigin = PointToScreen(e.GetPosition(this));
        _resizeW = Width;
        _resizeH = Height;
        CaptureMouse();
        e.Handled = true;
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
        _settings.Width = Width;
        _settings.Height = Height;
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
