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
    /// <summary>自动沉降：点卡片浮到最上，点别处沉到桌面（对齐优效等插件的层级交互）。</summary>
    public bool AutoSink { get; set; } = true;
    /// <summary>贴边隐藏：贴到屏幕边缘后缩成细条，鼠标靠近滑出。</summary>
    public bool EdgeHide { get; set; } = false;
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

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static WeekSchedule? LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
                return JsonSerializer.Deserialize<WeekSchedule>(File.ReadAllText(CachePath));
        }
        catch { }
        return null;
    }

    public static void SaveCache(WeekSchedule sched)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(sched));
        }
        catch { }
    }
}
