using System.Net;
using System.Net.Http;
using HtmlAgilityPack;

namespace AnimeWidget;

/// <summary>
/// 抓取 + 解析 AGE 动漫周表：双镜像 failover、系统代理、HtmlAgilityPack 解析。
/// 移植自 Rust 版 schedule.rs（scraper → HtmlAgilityPack）。
/// </summary>
public class ScheduleService : IDisposable
{
    public static readonly string[] Mirrors = { "https://www.agedm.io", "https://www.age.tv" };
    public static readonly string[] WeekdayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private string _proxyDesc;
    private CancellationTokenSource? _cts;
    private int _intervalMinutes = 30;

    public WeekSchedule? Current { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastOk { get; private set; }

    public event Action<WeekSchedule>? ScheduleUpdated;
    public event Action? StateChanged;

    public ScheduleService()
    {
        _proxyDesc = ProxyDetect.Detect() ?? "直连";
    }

    /// <summary>运行时改刷新间隔（下一轮循环生效）。</summary>
    public void SetInterval(int minutes) => _intervalMinutes = minutes;

    /// <summary>
    /// 每次抓取新建 client：代理设置实时重读（OpenClash 开关不用重启），
    /// 30 分钟级抓取频率不需要 keep-alive。
    /// </summary>
    private HttpClient BuildClient()
    {
        var proxy = ProxyDetect.Detect();
        var handler = new HttpClientHandler();
        if (proxy != null)
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
            _proxyDesc = proxy;
        }
        else
        {
            handler.UseProxy = false;
            _proxyDesc = "直连";
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        return client;
    }

    public string ProxyDesc => _proxyDesc;

    /// <summary>启动：先用缓存填充（离线也有数据），再后台刷新。</summary>
    public void Start(int refreshMinutes)
    {
        var cached = AppSettings.LoadCache();
        if (cached != null)
        {
            Current = cached;
            ScheduleUpdated?.Invoke(cached);
        }
        _cts = new CancellationTokenSource();
        _intervalMinutes = refreshMinutes;
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public void RefreshNow() => _ = Task.Run(() => FetchAndPublish(_cts?.Token ?? default));

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await FetchAndPublish(ct);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(5, _intervalMinutes)), ct);
            }
            catch (TaskCanceledException) { }
        }
    }

    private async Task FetchAndPublish(CancellationToken ct)
    {
        using var client = BuildClient(); // 每次重读代理设置
        var errors = new List<string>();
        foreach (var baseUrl in Mirrors)
        {
            try
            {
                var html = await client.GetStringAsync(baseUrl, ct);
                var sched = Parse(html, baseUrl);
                if (sched.Days.Any(d => d.Entries.Count > 0))
                {
                    Current = sched;
                    LastError = null;
                    LastOk = DateTime.Now;
                    AppSettings.SaveCache(sched);
                    ScheduleUpdated?.Invoke(sched);
                    StateChanged?.Invoke();
                    return;
                }
                errors.Add($"{baseUrl} → 页面无周表数据");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{baseUrl} → {ex.Message}");
            }
        }
        LastError = string.Join("\n", errors);
        StateChanged?.Invoke();
    }

    /// <summary>解析 agedm 首页。pane id: week-1..week-6 = 周一~周六，week-0 = 周日。</summary>
    public static WeekSchedule Parse(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var days = new List<DaySchedule>(7);
        for (var weekday = 0; weekday < 7; weekday++)
        {
            var paneId = weekday == 6 ? 0 : weekday + 1;
            var pane = doc.GetElementbyId($"week-{paneId}-pane");
            var entries = new List<Entry>();
            if (pane != null)
            {
                foreach (var li in pane.SelectNodes(".//li") ?? Enumerable.Empty<HtmlNode>())
                {
                    var isEndClass = li.GetAttributeValue("class", "").Contains("episode_end");
                    var a = li.SelectSingleNode(".//a[contains(@href,'/detail/')]");
                    if (a == null) continue;
                    var title = HtmlEntity.DeEntitize(a.InnerText).Trim();
                    if (title.Length == 0) continue;

                    var href = a.GetAttributeValue("href", "");
                    var detailId = href.Split("/detail/").LastOrDefault()?.Trim('/') ?? "";

                    var newNode = li.SelectSingleNode(".//*[contains(@class,'title_new')]");
                    var isNew = newNode != null && newNode.InnerText.Contains("New");

                    var subRaw = li.SelectSingleNode(".//*[contains(@class,'title_sub')]") is { } sub
                        ? HtmlEntity.DeEntitize(sub.InnerText).Trim()
                        : "";
                    var (time, label) = SplitTime(subRaw);
                    var isEnd = isEndClass || label.Contains("完结");

                    entries.Add(new Entry
                    {
                        Title = title,
                        DetailId = detailId,
                        IsNew = isNew,
                        IsEnd = isEnd,
                        Time = time,
                        Label = label,
                    });
                }
            }
            days.Add(new DaySchedule { Weekday = weekday, Entries = entries });
        }

        return new WeekSchedule
        {
            Base = baseUrl.TrimEnd('/'),
            Days = days,
            FetchedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
    }

    /// <summary>"23:00 第04集" -> ("23:00", "第04集")；"第11集(完结)" -> (null, ...)</summary>
    public static (string? Time, string Label) SplitTime(string sub)
    {
        sub = sub.Trim();
        var idx = sub.IndexOfAny(new[] { ' ', '　', '\t' });
        if (idx > 0)
        {
            var head = sub[..idx];
            if (IsHhmm(head))
                return (head, sub[(idx + 1)..].Trim());
        }
        return (null, sub);
    }

    private static bool IsHhmm(string s)
    {
        var parts = s.Split(':');
        return parts.Length == 2
            && parts[0].Length == 2 && parts[1].Length == 2
            && parts.All(p => p.All(char.IsAsciiDigit));
    }

    /// <summary>今天是周几：0=周一 … 6=周日。</summary>
    public static int TodayIndex()
    {
        var d = (int)DateTime.Now.DayOfWeek; // 周日=0
        return d == 0 ? 6 : d - 1;
    }

    public void Dispose()
    {
        _cts?.Cancel();
    }
}
