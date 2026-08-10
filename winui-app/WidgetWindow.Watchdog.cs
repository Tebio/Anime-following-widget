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
// ---------- 僵尸自愈看门狗（移植 v3.12.0/3.16.1） ----------
    // 整理软件重建桌面层会连坐销毁 hwnd；"应显示却不可见"也会被拉回。
    // "消失救不回只能重启" 从结构上消灭。

    private static WidgetWindow? _current;
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _dog;
    private bool _exiting;

    private void StartWatchdog()
    {
        if (_dog != null) return;
        _dog = DispatcherQueue.CreateTimer();
        _dog.Interval = TimeSpan.FromSeconds(5);
        _dog.Tick += (_, _) =>
        {
            var cur = _current;
            if (cur == null || cur._exiting) return;
            if (!IsWindow(cur._hwnd))
            {
                BootLog.Log("看门狗：hwnd 已死，重建窗口");
                try { cur._sched.Dispose(); } catch { }
                try { cur.TrayIcon.Dispose(); } catch { }
                _current = new WidgetWindow();
                _current.Activate();
                return;
            }
            // 应显示却被搞没了（整理软件/收纳层冲突）→ 拉回
            if (cur._visible && !cur._hoverHidden && !IsWindowVisible(cur._hwnd))
            {
                BootLog.Log("看门狗：窗口应显示但不可见，拉回");
                try { cur.AppWindow.Show(); } catch { }
            }
        };
        _dog.Start();
    }
}
