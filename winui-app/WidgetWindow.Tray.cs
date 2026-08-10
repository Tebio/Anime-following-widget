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
            BootLog.Log("Settings opened");
        }
        catch (Exception ex) { BootLog.Log("Settings FAIL: " + ex); }
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => ToggleVisibility();

    public void ToggleVisibility()
    {
        _hoverHidden = false; // 托盘/按钮显隐优先于悬停状态机，避免互相打架
        _visible = !_visible;
        if (_visible) AppWindow.Show(); else AppWindow.Hide();
    }

    private void Tray_Toggle(object sender, RoutedEventArgs e) => ToggleVisibility();

    private void Tray_Exit(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        TrayIcon.Dispose();
        Close();
    }
}
