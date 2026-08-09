using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;

namespace AnimeWidget.WinUI;

/// <summary>
/// 深色亚克力（DeskBox 同款 composition 控制器路线）。
/// XAML 的 DesktopAcrylicBackdrop 包装类不暴露 Tint 属性，必须用控制器手动接线。
/// </summary>
public static class BackdropHelper
{
    // 控制器必须保活，否则 GC 后材质消失
    private static readonly List<DesktopAcrylicController> _keepAlive = new();

    public static string ApplyDarkAcrylic(Window window)
    {
        try
        {
            if (Environment.OSVersion.Version.Build >= 22000 && MicaController.IsSupported())
            {
                window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
                {
                    Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt
                };
                return "Mica";
            }
            if (DesktopAcrylicController.IsSupported())
            {
                var target = window.As<ICompositionSupportsSystemBackdrop>();
                var config = new SystemBackdropConfiguration
                {
                    IsInputActive = true, // 桌面挂件失焦也保持材质
                    Theme = SystemBackdropTheme.Dark,
                };
                var controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
                if (!controller.AddSystemBackdropTarget(target)) return "Solid";
                controller.SetSystemBackdropConfiguration(config);
                var tint = Windows.UI.Color.FromArgb(255, 0x1F, 0x28, 0x38); // 优效灰蓝
                controller.TintColor = tint;
                controller.FallbackColor = tint;
                controller.TintOpacity = 0.45f;
                controller.LuminosityOpacity = 0.25f;
                _keepAlive.Add(controller);
                return "Acrylic";
            }
        }
        catch (Exception ex) { BootLog.Log("Backdrop fail: " + ex.Message); }
        return "Solid";
    }
}
