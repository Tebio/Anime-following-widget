using System.Diagnostics;
using System.Text;

namespace AnimeWidget;

/// <summary>
/// 运行中的桌面整理软件检测（酷呆 Coodesker / iTop Easy Desktop / Fences / Lively 等）。
/// 这类软件在桌面层（WorkerW/Progman）自建渲染表面，或提供「隐藏桌面」功能直接
/// 隐藏 WorkerW 子树——小组件嵌进 WorkerW 后会被盖住/随父层隐藏一起消失：
/// 症状 = 「显示一下就找不到，显示/隐藏开关也救不回来」。
/// 检测到它们时壁纸层嵌入自动降级为普通窗口。
///
/// 双通道检测：
/// ① 进程名名单（快，覆盖已知软件）；
/// ② 机制级：枚举顶层 WorkerW 窗口，凡不属于 explorer.exe 的 = 整理软件自建桌面层。
///    通道②不依赖任何名单——v3.11.0 用户实测 iTop 机器上名单漏判导致壁纸层僵尸，
///    通道②直接从冲突机制本身识别，漏不了。
/// </summary>
public static class OrganizerDetect
{
    private static readonly string[] Marks = { "coodesker", "itop", "fences", "lively", "deskgo", "360desktop" };

    public static bool AnyRunning() => ProcessNameHit() || ForeignWorkerWExists();

    private static bool ProcessNameHit()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    string name;
                    try { name = p.ProcessName.ToLowerInvariant(); }
                    catch { continue; }
                    foreach (var m in Marks)
                        if (name.Contains(m)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>存在不属于 explorer 的顶层 WorkerW = 有软件自建了桌面渲染层。</summary>
    private static bool ForeignWorkerWExists()
    {
        var foreign = false;
        var sb = new StringBuilder(64);
        Win32.EnumWindows((top, _) =>
        {
            sb.Clear();
            Win32.GetClassName(top, sb, sb.Capacity);
            if (sb.ToString() == "WorkerW" && !Win32.IsExplorerOwned(top))
            {
                foreign = true;
                return false; // 停止枚举
            }
            return true;
        }, IntPtr.Zero);
        return foreign;
    }
}
