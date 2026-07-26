using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeWidgetDesktop;

public sealed class WidgetSettings
{
    public string FeedUrl { get; set; } = "https://raw.githubusercontent.com/Tebio/Anime-following-widget/main/feed.sample.json";
    public int RefreshMinutes { get; set; } = 5;
    public int OpacityPercent { get; set; } = 92;
    public string Theme { get; set; } = "dark";
    public string AccentColor { get; set; } = "#8b5cf6";
    public bool AlwaysOnTop { get; set; } = true;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public int WindowWidth { get; set; } = 420;
    public int WindowHeight { get; set; } = 720;

    [JsonIgnore]
    public Color Accent => ColorTranslator.FromHtml(AccentColor);

    public static string FolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnimeFollowingWidget");
    public static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new WidgetSettings();
            }

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<WidgetSettings>(json, JsonOptions());
            return settings ?? new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
