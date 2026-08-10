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
        _visible = !_visible;
        if (_visible)
        {
            // 悬停 alpha 复位（3.16.1 语义：窗口从不移动，托盘只管 Show/Hide）
            _hoverAlpha = 255;
            _hoverHidden = false;
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
