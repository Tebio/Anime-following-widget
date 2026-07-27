using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
public enum ClickTarget { Detail, Search }

/// <summary>桌面嵌入方式。</summary>
public enum EmbedMode { WorkerW, BottomPin }

public class AppSettings
{
    public int Accent { get; set; } = 0;
    public double WindowOpacity { get; set; } = 0.95;
    public double BgDarkness { get; set; } = 0.85;
    public bool Locked { get; set; } = false;
    public bool ClickThrough { get; set; } = false;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public int RefreshMinutes { get; set; } = 30;
    public ClickTarget ClickTarget { get; set; } = ClickTarget.Detail;
    public EmbedMode EmbedMode { get; set; } = EmbedMode.WorkerW;

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
