using Microsoft.UI.Composition;
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
    private static readonly Dictionary<Window, DesktopAcrylicController> _byWindow = new();

    /// <summary>背景深浅 0(浅蓝灰)~1(深黑)，v3.16.1 同款公式</summary>
    public static Windows.UI.Color BgColor(double darkness)
    {
        int v = (int)(40 - 30 * Math.Clamp(darkness, 0, 1));
        return Windows.UI.Color.FromArgb(255, (byte)v, (byte)(v + 4), (byte)(v + 16));
    }

    /// <summary>界面效果：0=透明卡片 1=毛玻璃(Thin 弱着色) 2=亚克力(Base 深色)；opacity 影响着色强度</summary>
    public static string ApplyMaterial(Window window, int mode, double darkness = 0.55, double opacity = 0.9)
    {
        try
        {
            var tint = BgColor(darkness);
            float tintOp = mode == 1 ? (float)(0.10 + 0.30 * opacity) : (float)(0.20 + 0.55 * opacity);
            float lumOp = mode == 1 ? 0.55f : 0.25f;
            var wantKind = mode == 1 ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base;

            // 复用现有控制器（滑杆高频调整时只更新参数，不重建——重建会闪）
            if (mode != 0 && _byWindow.TryGetValue(window, out var existing) && !existing.IsClosed
                && existing.Kind == wantKind)
            {
                existing.TintColor = tint;
                existing.FallbackColor = tint;
                existing.TintOpacity = tintOp;
                existing.LuminosityOpacity = lumOp;
                return mode == 1 ? "Blur" : "Acrylic";
            }

            // 换档/首次：拆旧控制器
            if (_byWindow.Remove(window, out var old))
            {
                try { old.RemoveAllSystemBackdropTargets(); old.Dispose(); } catch { }
                _keepAlive.Remove(old);
            }
            window.SystemBackdrop = null;

            if (mode == 0) return "Transparent";

            if (mode == 2 && Environment.OSVersion.Version.Build >= 22000 && MicaController.IsSupported())
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
                var controller = new DesktopAcrylicController
                {
                    Kind = mode == 1 ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base
                };
                if (!controller.AddSystemBackdropTarget(target)) return "Solid";
                controller.SetSystemBackdropConfiguration(config);
                controller.TintColor = tint;
                controller.FallbackColor = tint;
                controller.TintOpacity = tintOp;
                controller.LuminosityOpacity = lumOp;
                _keepAlive.Add(controller);
                _byWindow[window] = controller;
                return mode == 1 ? "Blur" : "Acrylic";
            }
        }
        catch (Exception ex) { BootLog.Log("Backdrop fail: " + ex.Message); }
        return "Solid";
    }
}
