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

public partial class WidgetWindow
{
    // ---------- 托盘（菜单代码构建，抄 DeskBox：XAML 声明的 flyout 项在窗口透明/隐藏态下 Click 不触发） ----------

    public void SetupTray()
    {
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };

        var toggle = new MenuFlyoutItem { Text = "显示 / 隐藏" };
        toggle.Click += (_, _) => ToggleVisibility();

        var refresh = new MenuFlyoutItem { Text = "立即刷新" };
        refresh.Click += (_, _) => { StatusText.Text = "刷新中…"; _sched.RefreshNow(); };

        var settings = new MenuFlyoutItem { Text = "设置" };
        settings.Click += (_, _) => Settings_Click(null!, null!);

        var exit = new MenuFlyoutItem { Text = "退出" };
        exit.Click += (_, _) => Tray_Exit(null!, null!);

        menu.Items.Add(toggle);
        menu.Items.Add(refresh);
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);

        TrayIcon.ContextFlyout = menu;
        TrayIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleVisibility);
        try
        {
            var ico = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(ico)) TrayIcon.Icon = new System.Drawing.Icon(ico);
        }
        catch { }
        TrayIcon.ForceCreate();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BootLog.Log("Settings_Click enter, existing=" + (_settingsWin != null));
            if (_settingsWin != null) { _settingsWin.Activate(); return; }
            _settingsWin = new SettingsWindow(_settings, this);
            _settingsWin.Closed += (_, _) => _settingsWin = null;
            _settingsWin.Activate();
            // 顶到前台（无任务栏占位的应用 Activate 可能不前置，强制 SetForeground）
            var sw = Microsoft.UI.Win32Interop.GetWindowFromWindowId(_settingsWin.AppWindow.Id);
            SetForegroundWindow(sw);
            BootLog.Log("Settings opened");
        }
        catch (Exception ex) { BootLog.Log("Settings FAIL: " + ex); }
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => ToggleVisibility();

    public void ToggleVisibility()
    {
        _visible = !_visible;
        if (_visible)
        {
            // 立即复位透明度（不能只改字段——否则悬停状态机下一帧又淡回去，
            // 用户看到的就是"点了没反应"）；并给 3 秒强制可见宽限
            _hoverAlpha = CurrentNormalAlpha();
            _hoverHidden = false;
            _forceVisibleUntil = DateTime.UtcNow.AddSeconds(3);
            SetExStyle(WS_EX_LAYERED, true);
            SetLayeredWindowAttributes(_hwnd, 0, CurrentNormalAlpha(), LWA_ALPHA);
            AppWindow.Show();
        }
        else AppWindow.Hide();
    }

    private void Tray_Toggle(object sender, RoutedEventArgs e) => ToggleVisibility();

    private void Tray_Exit(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        TrayIcon.Dispose();
        Close();
    }
}
