using System.Diagnostics;

namespace AnimeWidget;

/// <summary>
/// 运行中的桌面整理软件检测（酷呆 Coodesker / iTop Easy Desktop / Fences / Lively 等）。
/// 这类软件在桌面层（WorkerW/Progman）自建渲染表面，或提供「隐藏桌面」功能直接
/// 隐藏 WorkerW 子树——小组件嵌进 WorkerW 后会被盖住/随父层隐藏一起消失：
/// 症状 = 「显示一下就找不到，显示/隐藏开关也救不回来」。
/// 检测到它们时壁纸层嵌入自动降级为普通窗口。
/// </summary>
public static class OrganizerDetect
{
    private static readonly string[] Marks = { "coodesker", "itop", "fences", "lively", "deskgo", "360desktop" };

    public static bool AnyRunning()
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
}
