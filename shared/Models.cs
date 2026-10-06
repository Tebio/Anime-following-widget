using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AnimeWidget;

/// <summary>一条放送记录。</summary>
public class Entry
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("detailId")] public string DetailId { get; set; } = "";
    [JsonPropertyName("isNew")] public bool IsNew { get; set; }
    [JsonPropertyName("isEnd")] public bool IsEnd { get; set; }
    /// <summary>"23:00"，无具体时刻为 null
    [JsonPropertyName("time")] public string? Time { get; set; }
    /// <summary>"第04集" / "PV" / "第11集(完结)"
    [JsonPropertyName("label")] public string Label { get; set; } = "";

    public string DetailUrl(string baseUrl) => $"{baseUrl}/detail/{DetailId}";
    public string SearchUrl(string baseUrl) =>
        $"{baseUrl}/search?query={Uri.EscapeDataString(Title)}";
    /// <summary>最新一集播放页：Label "第04集" → /play/{id}/1/4；无集数（PV 等）回退详情页。</summary>
    public string PlayUrl(string baseUrl)
    {
        var m = Regex.Match(Label, @"\d+");
        return m.Success && DetailId.Length > 0
            ? $"{baseUrl}/play/{DetailId}/1/{int.Parse(m.Value)}"
            : DetailUrl(baseUrl);
    }
}

public class DaySchedule
{
    /// <summary>0=周一 … 6=周日</summary>
    [JsonPropertyName("weekday")] public int Weekday { get; set; }
    [JsonPropertyName("entries")] public List<Entry> Entries { get; set; } = new();
}

public class WeekSchedule
{
    /// <summary>抓取成功的镜像 base（拼链接用）</summary>
    [JsonPropertyName("base")] public string Base { get; set; } = "";
    [JsonPropertyName("days")] public List<DaySchedule> Days { get; set; } = new();
    [JsonPropertyName("fetchedAt")] public string FetchedAt { get; set; } = "";
}

/// <summary>点击番名的行为。</summary>
public enum ClickTarget { Detail, Search, Play }

/// <summary>桌面嵌入方式。</summary>
public enum EmbedMode
{
    /// <summary>普通窗口：不碰 Progman/WorkerW，与 iTop/酷呆等桌面整理软件零冲突。</summary>
    Normal,
    /// <summary>挂到壁纸层 WorkerW：Win+D 不消失（可能与桌面整理软件冲突）。</summary>
    WorkerW,
    /// <summary>置底窗口：普通窗口压到最底。</summary>
    BottomPin,
}

public class AppSettings
{
    public int Accent { get; set; } = 0;
    public double WindowOpacity { get; set; } = 0.95;
    public double BgDarkness { get; set; } = 0.55;
    /// <summary>磨砂背景（系统亚克力）。默认关：干净单层卡片，用户自选。</summary>
    public bool BlurEnabled { get; set; } = false;
    /// <summary>界面效果：0=无 1=毛玻璃 2=亚克力（v3.12.1 起取代 BlurEnabled；旧值 true 迁移为 2）。</summary>
    public int BlurMode { get; set; } = 0;
    /// <summary>自动沉降：点卡片浮到最上，点别处沉到桌面（对齐优效等插件的层级交互）。</summary>
    public bool AutoSink { get; set; } = true;
    /// <summary>贴边隐藏：贴到屏幕边缘后缩成细条，鼠标靠近滑出。</summary>
    public bool EdgeHide { get; set; } = false;
    /// <summary>隐身模式（酷呆同款）：平时完全透明，鼠标探到卡片所在区域才浮现。</summary>
    public bool HoverReveal { get; set; } = false;
    public bool Locked { get; set; } = false;
    public bool ClickThrough { get; set; } = false;
    public bool Topmost { get; set; } = false;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public int RefreshMinutes { get; set; } = 30;
    public bool NotifyOnAir { get; set; } = true;
    /// <summary>收藏的番（DetailId 集合），到点提醒只针对收藏。</summary>
    public List<string> Favorites { get; set; } = new();
    public bool FavoritesOnly { get; set; } = false;
    public ClickTarget ClickTarget { get; set; } = ClickTarget.Play;
    public EmbedMode EmbedMode { get; set; } = EmbedMode.Normal;

    /// <summary>强调色预设（与 Rust 版一致）。</summary>
    public static readonly (string Name, byte R, byte G, byte B)[] Accents =
    {
        ("青绿", 45, 212, 191),
        ("香槟金", 229, 192, 123),
        ("雾紫", 179, 157, 219),
        ("樱粉", 244, 143, 177),
        ("暖橙", 255, 171, 145),
    };

    public (byte R, byte G, byte B) AccentRgb =>
        Accent >= 0 && Accent < Accents.Length
            ? (Accents[Accent].R, Accents[Accent].G, Accents[Accent].B)
            : (Accents[0].R, Accents[0].G, Accents[0].B);

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnimeFollowingWidget");
    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string CachePath => Path.Combine(Dir, "cache.json");
    /// <summary>上一份成功写入的设置（File.Replace 自动维护）；主文件损坏时从它恢复。</summary>
    public static string SettingsBakPath => SettingsPath + ".bak";
    /// <summary>损坏的主文件会被改名保留到这里，供事后排查。</summary>
    public static string SettingsCorruptPath => SettingsPath + ".corrupt";

    /// <summary>最近一次 Load 的提示（损坏恢复 / 回退默认值），正常为 null。</summary>
    public static string? LastLoadNotice { get; private set; }
    /// <summary>最近一次写盘失败的原因，成功后清空。</summary>
    public static string? LastSaveError { get; private set; }

    // ---------- 持久化实现 ----------
    // 要点：1) 先写 .tmp 再原子替换，崩溃/断电不会留下半截 JSON；
    //      2) Load 失败不再静默回退默认值——损坏文件改名留存，并优先用 .bak 恢复；
    //      3) Save() 合并写盘（节流 300ms，取最新快照），设置页滑杆连拖不再每次都写盘；
    //         进程正常退出（ProcessExit）与 Load() 前都会 Flush，不丢最后一次修改。

    private static readonly object _io = new();
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private const int SaveDelayMs = 300;
    private static string? _pendingJson;
    private static bool _armed;
    private static bool _exitHooked;
    private static System.Threading.Timer? _timer;

    private enum ReadState { Missing, Ok, Corrupt }

    private static ReadState ReadJson<T>(string path, out T? value) where T : class
    {
        value = null;
        try
        {
            if (!File.Exists(path)) return ReadState.Missing;
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            return value != null ? ReadState.Ok : ReadState.Corrupt;
        }
        catch
        {
            return ReadState.Corrupt;
        }
    }

    /// <summary>先写临时文件再原子替换目标；bakPath 非空时旧版本留作备份。短暂的 IO 占用（杀软/同步盘）重试 3 次。</summary>
    private static void WriteAtomic(string path, string content, string? bakPath)
    {
        Directory.CreateDirectory(Dir);
        var tmp = path + ".tmp";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, content);
                if (File.Exists(path))
                {
                    if (bakPath != null) File.Replace(tmp, path, bakPath);
                    else File.Move(tmp, path, overwrite: true);
                }
                else
                {
                    File.Move(tmp, path);
                    // 主文件不存在说明是首次写入或用户手动删除来重置：旧备份不应再被当作恢复源
                    if (bakPath != null) { try { File.Delete(bakPath); } catch { } }
                }
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }

    public static AppSettings Load()
    {
        Flush(); // 有未落盘的修改（如看门狗重建窗口）先写下去，避免读到旧文件
        lock (_io)
        {
            LastLoadNotice = null;
            var state = ReadJson<AppSettings>(SettingsPath, out var s);
            if (state == ReadState.Ok) return s!;
            if (state == ReadState.Missing) return new(); // 首次运行或用户手动删除来重置

            // 主文件存在但读不出来：改名留存，不让下一次 Save 把它盖掉
            try { File.Move(SettingsPath, SettingsCorruptPath, overwrite: true); } catch { }
            if (ReadJson<AppSettings>(SettingsBakPath, out var bak) == ReadState.Ok)
            {
                LastLoadNotice = "settings.json 已损坏，已从 settings.json.bak 恢复（原文件保留为 settings.json.corrupt）";
                return bak!;
            }
            LastLoadNotice = "settings.json 已损坏且无可用备份，已使用默认设置（原文件保留为 settings.json.corrupt）";
            return new();
        }
    }

    /// <summary>保存设置。合并写盘：300ms 内的多次调用只落最后一份快照；需要立刻落盘用 <see cref="Flush"/>。</summary>
    public void Save()
    {
        string json;
        try
        {
            // 在调用线程（UI）上序列化，保证快照一致；定时器线程只负责写盘
            json = JsonSerializer.Serialize(this, _json);
        }
        catch (Exception ex)
        {
            LastSaveError = "设置序列化失败: " + ex.Message;
            return;
        }
        lock (_io)
        {
            _pendingJson = json;
            if (!_exitHooked)
            {
                _exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            }
            if (_armed) return;
            _armed = true;
            _timer ??= new System.Threading.Timer(_ => Flush(), null,
                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _timer.Change(SaveDelayMs, System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>把待写的最新快照立刻写盘（无待写内容时什么都不做）。</summary>
    public static void Flush()
    {
        lock (_io)
        {
            _armed = false;
            var json = _pendingJson;
            _pendingJson = null;
            if (json == null) return;
            try
            {
                WriteAtomic(SettingsPath, json, SettingsBakPath);
                LastSaveError = null;
            }
            catch (Exception ex)
            {
                LastSaveError = "保存设置失败: " + ex.Message;
            }
        }
    }

    public static WeekSchedule? LoadCache()
    {
        lock (_io)
        {
            // 缓存损坏无所谓：返回 null，下一轮抓取会重建
            return ReadJson<WeekSchedule>(CachePath, out var c) == ReadState.Ok ? c : null;
        }
    }

    public static void SaveCache(WeekSchedule sched)
    {
        try
        {
            var json = JsonSerializer.Serialize(sched);
            lock (_io) WriteAtomic(CachePath, json, null);
        }
        catch { }
    }
}
