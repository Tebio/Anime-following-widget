using Microsoft.UI.Xaml;

namespace AnimeWidget.WinUI;

/// <summary>启动期逐步日志：每次崩溃后看最后一行就知道死在哪个环节。</summary>
public static class BootLog
{
    private static readonly string Path_ = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "animewidget_v4_boot.log");

    public static void Log(string step)
    {
        try { System.IO.File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss.fff} {step}\n"); }
        catch { }
    }
}

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        BootLog.Log("App.ctor enter");
        InitializeComponent();
        BootLog.Log("InitializeComponent ok");

        UnhandledException += (_, e) => BootLog.Log("UNHANDLED(XAML): " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            BootLog.Log("UNHANDLED(AppDomain): " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            BootLog.Log("UNOBSERVED(Task): " + e.Exception);
        BootLog.Log("App.ctor done");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        BootLog.Log("OnLaunched enter");
        _window = new MinimalWindow(); // 二分定位：先验证框架，再验证 WidgetWindow
        BootLog.Log("MinimalWindow created");
        _window.Activate();
        BootLog.Log("Activate ok");
    }
}
