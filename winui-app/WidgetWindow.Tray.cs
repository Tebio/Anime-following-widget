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
    // ---------- 托盘 / 显隐 ----------

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
