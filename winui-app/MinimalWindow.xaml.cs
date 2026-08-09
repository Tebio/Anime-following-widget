using Microsoft.UI.Xaml;

namespace AnimeWidget.WinUI;

public partial class MinimalWindow : Window
{
    public MinimalWindow()
    {
        BootLog.Log("MinimalWindow.ctor enter");
        InitializeComponent();
        BootLog.Log("MinimalWindow xaml ok");
    }
}
