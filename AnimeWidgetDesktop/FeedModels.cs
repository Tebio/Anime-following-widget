using System.Text.Json.Serialization;

namespace AnimeWidgetDesktop;

public sealed class AnimeFeed
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public List<AnimeFeedItem> Items { get; set; } = [];
}

public sealed class AnimeFeedItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("episode")]
    public string? Episode { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("badge")]
    public string? Badge { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("watchUrl")]
    public string? WatchUrl { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}
