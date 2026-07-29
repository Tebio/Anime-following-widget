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
        EnsureOnScreen();
        // 不再把 WindowOpacity 应用到整窗 Opacity（文字会跟着发灰）——
        // 通透感 = 背景半透 + 文字 100% 不透明，由 ApplyBgDarkness 折进背景 alpha。
        Opacity = 1.0;

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
        _vm.Favorites = new HashSet<string>(_settings.Favorites);
        _vm.FavoritesOnly = _settings.FavoritesOnly;
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
        // 只提醒收藏的番
        var favCrossed = crossed.Where(e => _settings.Favorites.Contains(e.DetailId)).ToList();
        if (favCrossed.Count == 0) return;
        var names = string.Join("、", favCrossed.Take(3).Select(e => e.Title));
        if (favCrossed.Count > 3) names += $" 等 {favCrossed.Count} 部";
        _tray?.ShowBalloonTip("番剧更新",
            $"{names} {favCrossed[0].Label} 已播出",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.None);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyBlur(); // 磨砂与否由设置决定（默认关）
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc); // 四边/四角原生拉伸 + 缩放结束持久化
        // 整理软件在运行时不挂 WorkerW（会被它们的桌面表面盖住/随父层隐藏消失）
        var want = _settings.EmbedMode;
        bool blocked = want == EmbedMode.WorkerW && OrganizerDetect.AnyRunning();
        _layer = new DesktopLayer(_hwnd, blocked ? EmbedMode.Normal : want);
        ApplyTopmost();
        ApplyClickThrough();
        StartInteractionTimer(); // 自动沉降 + 贴边隐藏（200ms 轮询）
        SetupTray();
        if (blocked) WarnOrganizer();
        StartLayerWatchdog();
    }

    /// <summary>Win+D / 「显示桌面」会把普通窗口最小化——桌面小组件要留在桌面上。</summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Show();
            _layer?.EnsureVisible();
        }
    }

    // ---------- 桌面层看门狗 + 整理软件冲突 ----------

    private bool _organizerWarned;

    private void WarnOrganizer()
    {
        if (_organizerWarned) return;
        _organizerWarned = true;
        _tray?.ShowBalloonTip("检测到桌面整理软件",
            "酷呆/iTop 等软件与壁纸层嵌入互相覆盖（小组件会被盖住找不到）。已自动切到普通窗口模式——普通模式现在也支持 Win+D 不消失。",
            BalloonIcon.Warning);
    }

    /// <summary>10s 巡检：整理软件出现自动让位、退出自动恢复、WorkerW 父层丢失重挂。</summary>
    private void StartLayerWatchdog()
    {
        var dog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        dog.Tick += (_, _) =>
        {
            if (_layer == null || _settings.EmbedMode != EmbedMode.WorkerW) return;
            bool organizer = OrganizerDetect.AnyRunning();
            if (organizer && _layer.Mode == EmbedMode.WorkerW)
            {
                _layer.SetMode(EmbedMode.Normal);
                ApplyTopmost();
                WarnOrganizer();
            }
            else if (!organizer && _layer.Mode != EmbedMode.WorkerW)
            {
                _layer.SetMode(EmbedMode.WorkerW);
                ApplyTopmost();
            }
            else
            {
                _layer.EnsureParented();
            }
        };
        dog.Start();
    }

    /// <summary>置顶只在普通窗口模式下生效（嵌入桌面层后无意义）。</summary>
    private void ApplyTopmost()
    {
        Topmost = _settings.Topmost && (_layer?.Mode ?? EmbedMode.Normal) == EmbedMode.Normal;
        // 右下角手动缩放手柄只在 WorkerW 模式出现（该模式原生边缘拉伸不可用）；
        // 普通/置底模式四边四角直接拖，手柄纯属多余
        ResizeGrip.Visibility = (_layer?.Mode ?? EmbedMode.Normal) == EmbedMode.WorkerW
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 外观 ----------

    /// <summary>还原位置若大半落在可视区域外（拔掉副屏等），拉回主屏默认位，防「小组件消失找不到」。</summary>
    private void EnsureOnScreen()
    {
        var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var win = new Rect(Left, Top, Width, Height);
        var visible = Rect.Intersect(vs, win);
        if (visible.IsEmpty || visible.Width * visible.Height < win.Width * win.Height / 4)
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24;
            Top = wa.Top + 80;
        }
    }

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
        // v3.10.x 起双滑条正交：「窗口透明度」= 纯面板 alpha，「背景深浅」= 纯面板色相。
        // 磨砂开启时背景由自截屏模糊掌管（RefreshBlur），这里不覆盖。
        if (_settings.BlurEnabled) return;
        var d = Math.Clamp(_settings.BgDarkness, 0, 1);
        var v = (byte)(40 - d * 24);              // 40(#242838 优效色系) → 16(近黑)
        var a = (byte)Math.Min(255, 255 * Math.Clamp(_settings.WindowOpacity, 0.08, 1));
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(a, (byte)(v - 4), v, (byte)(v + 16)));
    }

    // ---------- 自截屏磨砂（v3.11.0 替代系统 ACCENT——后者对透明 WPF 窗口要么失效要么"加个底"） ----------

    private readonly DispatcherTimer _blurTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private void ApplyBlur()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.DisableAcrylic(_hwnd); // 清掉旧版 ACCENT 残留（"卡片后面多个底"的源头）
        if (_settings.BlurEnabled)
        {
            RefreshBlur();
            _blurTimer.Tick -= BlurTimer_Tick;
            _blurTimer.Tick += BlurTimer_Tick;
            _blurTimer.Start();
        }
        else
        {
            _blurTimer.Stop();
            ApplyBgDarkness(); // 回纯色面板
        }
    }

    private void BlurTimer_Tick(object? sender, EventArgs e) => RefreshBlur();

    // 抓取+模糊全在后台线程（v3.11.1 的 50ms 等待在 UI 线程 = 用户实测"移动会卡顿"），
    // UI 线程只换刷子；拖动中 120ms 节流实时刷新 ≈ 优效"透视随时更新"的体感。
    private bool _blurBusy;
    private DateTime _lastBlurAt = DateTime.MinValue;

    private void RefreshBlur(bool throttle = false)
    {
        if (!_settings.BlurEnabled || _hwnd == IntPtr.Zero || _layer?.Mode == EmbedMode.WorkerW) return;
        if (_blurBusy) return;
        if (throttle && (DateTime.UtcNow - _lastBlurAt).TotalMilliseconds < 120) return;
        _blurBusy = true;
        _lastBlurAt = DateTime.UtcNow;
        var hwnd = _hwnd;
        Win32.GetWindowRect(hwnd, out var r);
        System.Threading.Tasks.Task.Run(() =>
        {
            var img = SelfBlur.CaptureBlurred(hwnd, r);
            Dispatcher.Invoke(() =>
            {
                _blurBusy = false;
                if (img != null && _settings.BlurEnabled)
                    RootBorder.Background = new ImageBrush(img) { Stretch = Stretch.Fill };
            });
        });
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
            var txt = new TextBlock
            {
                Text = ScheduleService.WeekdayNames[i],
                FontSize = 12,
                Effect = (System.Windows.Media.Effects.Effect)FindResource("TextShadow"),
            };
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

    /// <summary>星标点击：切换收藏（嵌套 Button，不会触发行跳转）。</summary>
    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id || id.Length == 0) return;
        if (_settings.Favorites.Contains(id)) _settings.Favorites.Remove(id);
        else _settings.Favorites.Add(id);
        _settings.Save();
        _vm.Favorites = new HashSet<string>(_settings.Favorites);
        _vm.RefreshEntries();
        e.Handled = true;
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
        else
        {
            Show();
            _layer?.EnsureVisible();
            if ((_layer?.Mode ?? EmbedMode.Normal) == EmbedMode.Normal) Activate();
        }
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
            ApplyAccent();
            ApplyBgDarkness(); // WindowOpacity=纯面板alpha、BgDarkness=纯色相
            ApplyBlur();       // 磨砂开关即时生效
        };
        _settingsWin.EmbedModeChanged += mode =>
        {
            // 用户偏好保留 WorkerW，运行时遇整理软件降级（看门狗会在整理软件退出后自动恢复）
            if (mode == EmbedMode.WorkerW && OrganizerDetect.AnyRunning())
            {
                mode = EmbedMode.Normal;
                WarnOrganizer();
            }
            _layer?.SetMode(mode);
            ApplyTopmost();
        };
        _settingsWin.ListRefreshNeeded += () =>
        {
            _vm.FavoritesOnly = _settings.FavoritesOnly;
            _vm.RefreshEntries();
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

    // 手动拖拽：WorkerW 子窗口没有标题栏，DragMove() 会失效，必须自己算坐标。
    // ⚠️ 不能在 MouseDown 就 CaptureMouse——捕获会把后续鼠标事件全部重定向到窗口，
    // 星期 tab / 错误横幅这类 Border 的 MouseLeftButtonUp 永远收不到（点击被吞）。
    // 改为「按下记位，移动超 4px 才捕获并开始拖拽」：原地松手 = 正常点击，事件照常路由。
    private bool _dragging;
    private bool _dragPending;
    private Point _dragOffset;
    private Point _dragStart; // 按下时的屏幕坐标（物理像素）
    private double _dragLogicalX, _dragLogicalY; // 拖拽中的目标逻辑坐标（松手才回写 DP）

    private void Root_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is Button) return;
        if (_settings.Locked) return;
        _dragPending = true;
        _dragOffset = e.GetPosition(this);
        _dragStart = PointToScreen(_dragOffset);
        // 不捕获、不 Handled：原地松手时 tab 等子元素能正常收到 MouseLeftButtonUp
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed) { _dragPending = false; return; }
        // 直接读屏幕物理坐标：窗口移动中 PointToScreen(e.GetPosition(this)) 的
        // 坐标原点跟着窗口走，会形成反馈环——这就是拖拽不跟手的根因。
        Win32.GetCursorPos(out var cur);
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        if (_resizing)
        {
            double w = Math.Max(MinWidth, _resizeW + (cur.X - _resizeOrigin.X) / sx);
            double h = Math.Max(MinHeight, _resizeH + (cur.Y - _resizeOrigin.Y) / sy);
            _resizeTargetW = w; _resizeTargetH = h;
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0,
                (int)Math.Round(w * sx), (int)Math.Round(h * sy),
                Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            return;
        }
        if (_dragPending)
        {
            if (Math.Abs(cur.X - _dragStart.X) < 4 && Math.Abs(cur.Y - _dragStart.Y) < 4) return;
            _dragPending = false;
            _dragging = true;
            CaptureMouse(); // 确认是拖拽后才捕获，此时点击判定已结束
        }
        if (!_dragging) return;
        _dragLogicalX = cur.X / sx - _dragOffset.X;
        _dragLogicalY = cur.Y / sy - _dragOffset.Y;
        // 直移 hwnd 不等 WPF 布局——松手时再回写 Left/Top DP。
        Win32.SetWindowPos(_hwnd, IntPtr.Zero,
            (int)Math.Round(_dragLogicalX * sx), (int)Math.Round(_dragLogicalY * sy), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        if (_settings.BlurEnabled) RefreshBlur(throttle: true); // 拖动中实时透视（后台线程不卡）
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragPending = false;
        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();
            Width = _resizeTargetW; Height = _resizeTargetH; // 回写 DP（视觉无跳变）
            Persist();
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Left = _dragLogicalX; Top = _dragLogicalY; // 回写 DP 后吸附/持久化才有正确基准
        SnapToEdges();
        Persist();
        if (_settings.BlurEnabled) RefreshBlur(); // 拖完重抓身后壁纸
    }

    private const double SnapDist = 20;
    private const double EdgeMargin = 8;

    // ---------- 四边/四角原生拉伸（Normal/BottomPin 模式） ----------
    // 无边框窗口默认全窗 HTCLIENT；命中边缘 7px 带时改报 HT* 方向码，
    // Windows 接管模态缩放循环——手感与原生窗口一致，且上下左右四角全支持。
    // WorkerW 子窗口的非客户消息不可靠，该模式仍用右下角手柄（Grip_Resize）。
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_MOVING = 0x0216;
    private const int WM_SIZING = 0x0214;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_EXITSIZEMOVE)
        {
            Persist(); // 原生缩放/移动结束 → 尺寸位置落盘
            if (_settings.BlurEnabled) RefreshBlur(); // 位置/尺寸变了 → 重抓身后壁纸
            return IntPtr.Zero;
        }
        if ((msg == WM_MOVING || msg == WM_SIZING) && _settings.BlurEnabled)
        {
            RefreshBlur(throttle: true); // 原生拖动/缩放中也实时透视
            return IntPtr.Zero;
        }
        if (msg != WM_NCHITTEST) return IntPtr.Zero;
        if (_settings.Locked || _settings.ClickThrough) return IntPtr.Zero;
        if ((_layer?.Mode ?? EmbedMode.Normal) == EmbedMode.WorkerW) return IntPtr.Zero;

        // lParam = 屏幕坐标（物理像素，有符号 short 对）
        long lp = lParam.ToInt64();
        int x = unchecked((short)(lp & 0xFFFF));
        int y = unchecked((short)((lp >> 16) & 0xFFFF));
        Win32.GetWindowRect(hwnd, out var r);
        var dpi = VisualTreeHelper.GetDpi(this);
        int grip = Math.Max(4, (int)Math.Round(7 * dpi.DpiScaleX));

        bool left = x - r.Left >= 0 && x - r.Left < grip;
        bool right = r.Right - x > 0 && r.Right - x <= grip;
        bool top = y - r.Top >= 0 && y - r.Top < grip;
        bool bottom = r.Bottom - y > 0 && r.Bottom - y <= grip;

        int ht = 0;
        if (top && left) ht = 13;            // HTTOPLEFT
        else if (top && right) ht = 14;      // HTTOPRIGHT
        else if (bottom && left) ht = 16;    // HTBOTTOMLEFT
        else if (bottom && right) ht = 17;   // HTBOTTOMRIGHT
        else if (left) ht = 10;              // HTLEFT
        else if (right) ht = 11;             // HTRIGHT
        else if (top) ht = 12;               // HTTOP
        else if (bottom) ht = 15;            // HTBOTTOM
        if (ht == 0) return IntPtr.Zero;
        handled = true;
        return new IntPtr(ht);
    }

    /// <summary>周几 tab 条：滚轮 = 横向滚动。</summary>
    private void TabScroll_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }

    private void SnapToEdges()
    {
        var wa = SystemParameters.WorkArea;
        // ① 靠近边缘 20px → 吸附
        if (Math.Abs(Left - wa.Left) < SnapDist) Left = wa.Left + EdgeMargin;
        else if (Math.Abs(wa.Right - (Left + Width)) < SnapDist) Left = wa.Right - Width - EdgeMargin;
        if (Math.Abs(Top - wa.Top) < SnapDist) Top = wa.Top + EdgeMargin;
        else if (Math.Abs(wa.Bottom - (Top + Height)) < SnapDist) Top = wa.Bottom - Height - EdgeMargin;
        // ② 拖出屏幕 → 强制回弹到屏内（v3.11.0：用户实测"超边不回弹"）
        if (Left < wa.Left) Left = wa.Left + EdgeMargin;
        if (Left + Width > wa.Right) Left = wa.Right - Width - EdgeMargin;
        if (Top < wa.Top) Top = wa.Top + EdgeMargin;
        if (Top + Height > wa.Bottom) Top = wa.Bottom - Height - EdgeMargin;
        // ③ 记录停靠边（贴边隐藏用）：0=无 1=左 2=右 3=上（底边不藏，任务栏侧）
        _dockEdge = 0;
        if (Math.Abs(Left - (wa.Left + EdgeMargin)) < 2) _dockEdge = 1;
        else if (Math.Abs(Left - (wa.Right - Width - EdgeMargin)) < 2) _dockEdge = 2;
        else if (Math.Abs(Top - (wa.Top + EdgeMargin)) < 2) _dockEdge = 3;
        if (_dockEdge == 0 && _edgeHidden) ShowFromEdge();
        _dockX = Left; _dockY = Top;
    }

    // ---------- 自动沉降 + 贴边隐藏（200ms 交互轮询） ----------

    private int _dockEdge;
    private bool _edgeHidden;
    private double _dockX, _dockY;
    private bool _lmbWasDown;
    private int _hideCountdown;

    private void StartInteractionTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        t.Tick += (_, _) => { AutoSinkTick(); EdgeHideTick(); };
        t.Start();
    }

    /// <summary>点卡片浮上来（系统激活天然完成），点别处沉到桌面。
    /// 优效等 NOACTIVATE 窗口被点不会触发我们失活 → 必须自己轮询点击沿+光标下根窗口。</summary>
    private void AutoSinkTick()
    {
        if (!_settings.AutoSink || _hwnd == IntPtr.Zero || _layer?.Mode != EmbedMode.Normal) return;
        if (_dragging || _resizing) return;
        bool down = (Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0;
        if (down && !_lmbWasDown) // 左键按下沿
        {
            Win32.GetCursorPos(out var pt);
            var root = Win32.GetAncestor(Win32.WindowFromPoint(pt), Win32.GA_ROOT);
            var settingsHwnd = _settingsWin?.IsVisible == true
                ? new System.Windows.Interop.WindowInteropHelper(_settingsWin).Handle : IntPtr.Zero;
            if (root != IntPtr.Zero && root != _hwnd && root != settingsHwnd)
            {
                Win32.SetWindowPos(_hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            }
        }
        _lmbWasDown = down;
    }

    private void EdgeHideTick()
    {
        if (_edgeHidden && (!_settings.EdgeHide || _layer?.Mode != EmbedMode.Normal || _dockEdge == 0))
            ShowFromEdge(); // 设置关了/模式变了 → 先弹回来
        if (!_settings.EdgeHide || _hwnd == IntPtr.Zero || _layer?.Mode != EmbedMode.Normal) return;
        if (_dockEdge == 0 || _dragging || _resizing) return;

        Win32.GetCursorPos(out var pt);
        var dpi = VisualTreeHelper.GetDpi(this);
        double cx = pt.X / dpi.DpiScaleX, cy = pt.Y / dpi.DpiScaleY;

        if (!_edgeHidden)
        {
            // 光标离开窗口矩形（外扩 6px）连续 ~1s → 藏起来
            bool over = cx >= Left - 6 && cx <= Left + Width + 6 && cy >= Top - 6 && cy <= Top + Height + 6;
            if (over) { _hideCountdown = 5; return; }
            if (--_hideCountdown > 0) return;
            _hideCountdown = 0;
            var wa = SystemParameters.WorkArea;
            _dockX = Left; _dockY = Top;
            if (_dockEdge == 1) Left = wa.Left - Width + 6;
            else if (_dockEdge == 2) Left = wa.Right - 6;
            else if (_dockEdge == 3) Top = wa.Top - Height + 6;
            _edgeHidden = true;
        }
        else
        {
            // 光标探到露出的细条附近 → 滑回
            var wa = SystemParameters.WorkArea;
            bool near = _dockEdge switch
            {
                1 => cx <= wa.Left + 16 && cy >= _dockY - 8 && cy <= _dockY + Height + 8,
                2 => cx >= wa.Right - 16 && cy >= _dockY - 8 && cy <= _dockY + Height + 8,
                3 => cy <= wa.Top + 16 && cx >= _dockX - 8 && cx <= _dockX + Width + 8,
                _ => false,
            };
            if (near) ShowFromEdge();
        }
    }

    private void ShowFromEdge()
    {
        if (!_edgeHidden) return;
        Left = _dockX; Top = _dockY;
        _edgeHidden = false;
        _hideCountdown = 8; // 刚滑出时给足宽限，别立刻又缩回去
    }

    // 手动缩放：WorkerW 子窗口下 WM_NCLBUTTONDOWN HTBOTTOMRIGHT 和 DragMove 一样失效
    private bool _resizing;
    private Point _resizeOrigin;
    private double _resizeW, _resizeH;
    private double _resizeTargetW, _resizeTargetH; // 缩放中的目标逻辑尺寸（松手回写 DP）

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
            : $"{ScheduleService.WeekdayNames[_vm.SelectedDay]} {count} 部"; // 代理是诊断信息，只留在设置→数据，别糊在主界面底栏
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
