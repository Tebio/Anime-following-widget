using Microsoft.Win32;

namespace AnimeWidget;

/// <summary>
/// 系统代理检测：HttpClient 默认走 IE 设置但 PAC/绕过规则复杂，
/// 这里显式读注册表（OpenClash 系统代理写的就是这里）+ env 兜底。
/// 移植自 Rust 版 proxy.rs。
/// </summary>
public static class ProxyDetect
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>返回可用的代理 URL（如 "http://127.0.0.1:7890"），无代理返回 null。</summary>
    public static string? Detect()
    {
        return RegistryProxy() ?? EnvProxy();
    }

    private static string? RegistryProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key == null) return null;
            if (key.GetValue("ProxyEnable") is not int enable || enable == 0) return null;
            if (key.GetValue("ProxyServer") is not string raw) return null;
            return ParseProxyServer(raw);
        }
        catch
        {
            return null;
        }
    }

    private static string? EnvProxy()
    {
        foreach (var name in new[] { "HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy" })
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(v))
                return WithScheme(v.Trim(), socks: false);
        }
        return null;
    }

    /// <summary>
    /// 解析注册表 ProxyServer：
    /// "127.0.0.1:7890" 或分协议 "http=1.2.3.4:8080;https=1.2.3.4:8080;socks=5.6.7.8:1080"
    /// </summary>
    public static string? ParseProxyServer(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return null;

        if (raw.Contains('='))
        {
            (int Rank, string Url)? best = null;
            foreach (var part in raw.Split(';'))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var k = part[..idx].Trim().ToLowerInvariant();
                var v = part[(idx + 1)..].Trim();
                if (v.Length == 0) continue;
                var rank = k switch { "https" => 0, "http" => 1, "socks" => 2, _ => 3 };
                if (best == null || rank < best.Value.Rank)
                    best = (rank, WithScheme(v, k == "socks"));
            }
            return best?.Url;
        }
        return WithScheme(raw, socks: false);
    }

    private static string WithScheme(string v, bool socks)
    {
        if (v.Contains("://")) return v;
        return socks ? $"socks5://{v}" : $"http://{v}";
    }
}
