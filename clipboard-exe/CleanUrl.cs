namespace ClipboardExe;

/// <summary>
/// URL 追踪参数清理——移植自 Web 版 lib/core/clips-store.js cleanUrl + TRACKING_KEYS。
/// 保留协议/主机/路径/其余参数，只剔除追踪参数（UTM/fbclid/gclid/igshid/from/spm 等 24 个）。
/// 无追踪参数时原样返回；畸形 URL 也原样返回。字符串级处理（不经过 Uri 重编码，保真）。
/// </summary>
public static class CleanUrl
{
    private static readonly HashSet<string> TrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "utm_id", "utm_source_platform", "utm_creative_format", "utm_marketing_tactic",
        "fbclid", "gclid", "gclsrc", "dclid", "msclkid", "mc_cid", "mc_eid",
        "igshid", "ref", "spm", "scm", "from", "from_sources", "clicktime", "clickid",
    };

    public static string Clean(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url; // 非 http(s) 不清理
        }

        var q = url.IndexOf('?');
        if (q < 0 || q == url.Length - 1) return url; // 无 query 或 query 为空 → 原样

        var basePart = url[..q];
        var kept = new List<string>();
        var removed = 0;

        foreach (var part in url[(q + 1)..].Split('&'))
        {
            if (part.Length == 0) continue;
            var key = part.Split('=', 2)[0].Trim();
            if (key.Length > 0 && TrackingKeys.Contains(key)) removed++;
            else kept.Add(part);
        }

        if (removed == 0) return url;
        return kept.Count == 0 ? basePart : basePart + "?" + string.Join("&", kept);
    }
}
