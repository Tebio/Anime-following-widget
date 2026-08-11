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
    // ---------- 托盘（菜单代码构建 + XamlUICommand） ----------
    // 上游已知 bug（H.NotifyIcon issue #92）：MenuFlyoutItem 的 Click 事件在托盘 flyout
    // 里不触发，必须走 Command/XamlUICommand 通道。

    public void SetupTray()
    {
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };

        MenuFlyoutItem Item(string text, string tag, Action act)
        {
            var cmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
            cmd.ExecuteRequested += (_, _) => { BootLog.Log("tray menu: " + tag); act(); };
            return new MenuFlyoutItem { Text = text, Command = cmd };
        }

        menu.Items.Add(Item("显示 / 隐藏", "toggle", ToggleVisibility));
        menu.Items.Add(Item("立即刷新", "refresh", () => { StatusText.Text = "刷新中…"; _sched.RefreshNow(); }));
        menu.Items.Add(Item("设置", "settings", () => Settings_Click(null!, null!)));
        menu.Items.Add(new MenuFlyoutSeparator());

        // 开机自启（移植 3.16.1：HKCU Run 键）
        var autoItem = new ToggleMenuFlyoutItem { Text = "开机自启", IsChecked = GetAutostart() };
        var autoCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
        autoCmd.ExecuteRequested += (_, _) =>
        {
            SetAutostart(!GetAutostart());
            autoItem.IsChecked = GetAutostart();
            BootLog.Log("autostart: " + GetAutostart());
        };
        autoItem.Command = autoCmd;
        menu.Items.Add(autoItem);

        menu.Items.Add(Item("退出", "exit", () => Tray_Exit(null!, null!)));

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

    // ---------- 开机自启（3.16.1 同款：HKCU Run 键） ----------

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
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (on) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValueName, false);
        }
        catch (Exception ex) { BootLog.Log("autostart fail: " + ex.Message); }
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
