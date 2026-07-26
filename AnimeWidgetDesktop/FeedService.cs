using System.Net.Http.Headers;
using System.Text.Json;

namespace AnimeWidgetDesktop;

public sealed class FeedService
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AnimeFollowingWidget", "1.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<AnimeFeed> LoadAsync(string? source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return BuildDemoFeed();
        }

        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https"))
            {
                using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Parse(json);
            }

            if (File.Exists(source))
            {
                var json = await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
                return Parse(json);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall through to demo feed.
        }

        return BuildDemoFeed();
    }

    private static AnimeFeed Parse(string json)
    {
        try
        {
            var feed = JsonSerializer.Deserialize<AnimeFeed>(json, JsonOptions);
            if (feed is { Items.Count: > 0 })
            {
                return feed;
            }
        }
        catch
        {
            // ignore parse errors
        }

        return BuildDemoFeed();
    }

    public static AnimeFeed BuildDemoFeed() => new()
    {
        Title = "本周放送列表",
        UpdatedAt = DateTimeOffset.Now,
        Items =
        [
            new AnimeFeedItem { Title = "Grow Up Show ～向日葵马戏团～", Badge = "New", Time = "23:00", Episode = "第04集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "黄泉使者", Badge = "New", Time = "00:00", Episode = "第16集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "穹庐下的魔女", Badge = "New", Time = "22:30", Episode = "第05集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "人造人009 涅墨西斯", Badge = "完结", Time = "—", Episode = "完结", Platform = "番剧库", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "声称不爱我的下任公爵为何会泪流…", Badge = "New", Time = "00:30", Episode = "第04集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "魔法少女奈叶 EXCEEDS Gun Blaze V…", Badge = "New", Time = "00:00", Episode = "第04集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "一叠间漫画咖啡屋生活！", Badge = "完结", Time = "—", Episode = "完结", Platform = "番剧库", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "花织同学转生后还是想干架", Badge = "New", Time = "01:30", Episode = "第03集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "鬼之花嫁", Badge = "New", Time = "23:30", Episode = "第04集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "才女的侍从 在满是高岭之花的贵族…", Badge = "New", Time = "01:38", Episode = "第04集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" },
            new AnimeFeedItem { Title = "炒翻天", Badge = "New", Time = "16:30", Episode = "第03集", Platform = "B站", WatchUrl = "https://www.bilibili.com/" }
        ]
    };
}
