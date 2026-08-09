using System.Runtime.InteropServices;
using System.Windows;

namespace AnimeWidget;

public partial class App : Application
{
    // 单文件 exe 嵌自定义 manifest 会崩（v3.16.3 教训），DPI 感知只能运行时设：
    // 必须在任何窗口创建之前调用，OnStartup 是第一时机。
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    protected override void OnStartup(StartupEventArgs e)
    {
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
