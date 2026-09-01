// Services/ClipService.cs - 条目业务规则（对齐 Web 版 clips-mutate.js createClip + clips-query.js listClips
// + public/app.js findDuplicateClip / getVisibleClips 拼音搜索）
// 纯函数 + Storage 编排，M2 零 UI；MainWindow 只做编排（边界契约 #1/#2：数据层独立文件）。
using System.Text.RegularExpressions;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

public sealed class ClipService
{
    public const int MaxTitle = 200;           // CONFIG.MAX_TITLE
    public const int MaxContent = 200 * 1024;  // CONFIG.MAX_CONTENT
    public const int MaxHtml = 512 * 1024;     // CONFIG.MAX_HTML
    public const int MaxTags = 10;             // CONFIG.MAX_TAGS
    public const int MaxTagLen = 20;           // CONFIG.MAX_TAG_LEN

    private static readonly Regex LinkRe = new(@"^https?://\S+$", RegexOptions.IgnoreCase);
    private static readonly Regex ExpireRe = new(@"^(\d+)([hd])$");

    private readonly Storage _storage;

    public ClipService(Storage storage) => _storage = storage;

    // ---- 创建（对齐 createClip：type 白名单 → 校验 → cleanUrl → 自动标题）----
    /// <param name="expire">过期选项 '1h'|'1d'|'7d'|'30d'|''(永久)。</param>
    public ClipItem Create(string type, string? title, string? content, string? html, string? url,
                           List<string>? tags, string? expire,
                           string? fileId = null, string? fileName = null, long fileSize = 0, string? fileMime = null)
    {
        var (t, tgs) = SanitizeInput(title, tags);
        var ty = type is "text" or "link" or "file" ? type : "text";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clip = new ClipItem
        {
            Id = Guid.NewGuid().ToString("D"),
            Type = ty,
            Title = t,
            Html = SanitizeHtml(html),
            Tags = tgs,
            ExpireAt = ResolveExpire(expire),
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (ty == "text")
        {
            var c = content ?? "";
            if (c.Length == 0) throw new InvalidOperationException("内容不能为空");
            if (c.Length > MaxContent) throw new InvalidOperationException("内容过长");
            clip.Content = c;
        }
        else if (ty == "link")
        {
            var u = url ?? "";
            if (!LinkRe.IsMatch(u)) throw new InvalidOperationException("链接需以 http(s):// 开头");
            clip.Url = CleanUrl.Clean(u); // v0.2.0：自动去追踪参数（UTM/fbclid 等）
            if (string.IsNullOrEmpty(clip.Title)) clip.Title = Truncate(clip.Url, 60); // v0.3.1：用清理后的 url 做标题，避免 utm 残留
        }
        else
        {
            if (string.IsNullOrEmpty(fileId)) throw new InvalidOperationException("缺少文件");
            clip.FileId = fileId;
            clip.FileName = Truncate(string.IsNullOrEmpty(fileName) ? "file" : fileName, 255);
            clip.FileSize = fileSize;
            clip.FileMime = fileMime ?? "";
        }
        var list = _storage.LoadClips();
        list.Add(clip);
        _storage.SaveClips(list);
        return clip;
    }

    // ---- 去重（对齐 app.js findDuplicateClip：link 比 url，其他比 content；url 兜底 "" 防 undefined 参与比对）----
    public static ClipItem? FindDuplicate(string? content, IEnumerable<ClipItem>? clips)
    {
        if (string.IsNullOrEmpty(content) || clips == null) return null;
        foreach (var c in clips)
        {
            var cmp = c.Type == "link" ? (c.Url ?? "") : (c.Content ?? "");
            if (cmp == content) return c;
        }
        return null;
    }

    // ---- 变更（对齐 Web 版 /api/clips/:id 系列：pin / copy / PUT / DELETE / archive）----
    /// <summary>按 id 取活跃区条目（弹窗回显用）。</summary>
    public ClipItem? GetById(string id)
        => _storage.LoadClips().FirstOrDefault(c => c.Id == id);

    /// <summary>置顶切换（对齐 POST /api/clips/:id/pin）。返回新状态。</summary>
    public bool TogglePin(string id)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) throw new InvalidOperationException("条目不存在");
        c.Pinned = !c.Pinned;
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // 置顶参与排序（对齐后端 pin 刷新排序）
        _storage.SaveClips(list);
        return c.Pinned;
    }

    /// <summary>复制计数 +1（对齐 POST /api/clips/:id/copy）。返回新计数。</summary>
    public long BumpCopyCount(string id)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) return 0;
        c.CopyCount++;
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _storage.SaveClips(list);
        return c.CopyCount;
    }

    /// <summary>删除活跃区条目（对齐 DELETE /api/clips/:id；文件实体由 M3b 联动清理）。</summary>
    public void Delete(string id)
    {
        var list = _storage.LoadClips();
        list.RemoveAll(c => c.Id == id);
        _storage.SaveClips(list);
    }

    /// <summary>移入归档（对齐 POST /api/clips/:id/archive：活跃区移除 + 追加归档）。</summary>
    public void Archive(string id)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) throw new InvalidOperationException("条目不存在");
        list.Remove(c);
        var arch = _storage.LoadArchive();
        if (!arch.Any(x => x.Id == id)) arch.Add(c);
        _storage.SaveArchive(arch);
        _storage.SaveClips(list);
    }

    /// <summary>从归档恢复到活跃区（对齐 Web ↺ 恢复；活跃区已存在则跳过避免重复）。</summary>
    public bool Unarchive(string id)
    {
        var arch = _storage.LoadArchive();
        var c = arch.FirstOrDefault(x => x.Id == id);
        if (c == null) throw new InvalidOperationException("归档中不存在");
        var list = _storage.LoadClips();
        if (list.Any(x => x.Id == id)) return false; // 已存在（理论上归档按 id 唯一，但活跃区可能有重——防御）
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // 恢复到活跃区参与排序
        list.Add(c);
        arch.Remove(c);
        _storage.SaveClips(list);
        _storage.SaveArchive(arch);
        return true;
    }

    /// <summary>
    /// 编辑保存（对齐 PUT /api/clips/:id）：title/tags/expire 恒更新；content（text/rich）或 url（link）按类型更新；
    /// html 仅富文本条目传入（由调用方决定：content 未变保留原值、变了 textToHtml 重建、清除格式传 ""）。
    /// 规则净化对齐 createClip（sanitizeInput + link 校验 + expire 解析）。
    /// </summary>
    public ClipItem? Update(string id, string? title, List<string>? tags, string? expire,
                            string? content = null, string? url = null, string? html = null)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) return null;
        var (t, tgs) = SanitizeInput(title, tags);
        c.Title = t;
        c.Tags = tgs;
        c.ExpireAt = ResolveExpire(expire);
        if (c.Type == "link")
        {
            var u = url ?? c.Url ?? "";
            if (!LinkRe.IsMatch(u)) throw new InvalidOperationException("链接需以 http(s):// 开头");
            c.Url = CleanUrl.Clean(u);
            if (string.IsNullOrEmpty(c.Title)) c.Title = Truncate(c.Url, 60);
        }
        else if (c.Type == "text")
        {
            if (content != null)
            {
                if (content.Length == 0) throw new InvalidOperationException("内容不能为空");
                if (content.Length > MaxContent) throw new InvalidOperationException("内容过长");
                c.Content = content;
            }
            if (html != null) c.Html = SanitizeHtml(html);
        }
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _storage.SaveClips(list);
        return c;
    }

    // ---- 查询（对齐 listClips + getVisibleClips 前端拼音过滤）----    /// <summary>q（标题/内容/URL/标签模糊 + 拼音首字母缩写）+ tag（精确）+ type + 含归档；自动过滤已过期。</summary>
    public List<ClipItem> Search(string? q, string? tag = "", string? type = "all", bool includeArchived = false)
    {
        var kw = (q ?? "").Trim().ToLowerInvariant();
        var tg = (tag ?? "").Trim();
        List<ClipItem> list;
        if (includeArchived)
        {
            // 归档合并：只取最近 ARCHIVE_SCAN_LIMIT 条（防超大归档拖慢），标记 archived 供前端只读
            var arch = _storage.LoadArchive();
            var tail = arch.Count > Storage.ArchiveScanLimit ? arch.Skip(arch.Count - Storage.ArchiveScanLimit).ToList() : arch;
            foreach (var c in tail) c.Archived = true;
            list = tail.Concat(_storage.LoadClips()).ToList();
        }
        else
        {
            list = _storage.LoadClips();
        }
        list = list.Where(c => !IsExpired(c)).ToList();
        if (!string.IsNullOrEmpty(type) && type != "all")
            list = list.Where(c => c.Type == type).ToList();
        if (tg.Length > 0)
            list = list.Where(c => c.Tags.Contains(tg)).ToList();
        if (kw.Length > 0)
        {
            list = list.Where(c =>
            {
                var title = c.Title ?? "";
                var content = c.Content ?? "";
                var url = c.Url ?? "";
                var tags = c.Tags ?? new List<string>();
                if (title.ToLowerInvariant().Contains(kw) ||
                    content.ToLowerInvariant().Contains(kw) ||
                    url.ToLowerInvariant().Contains(kw) ||
                    tags.Any(t => t.ToLowerInvariant().Contains(kw))) return true;
                // 拼音首字母缩写匹配（标题+标签）：如 "sf" → 身份
                var py = (Pinyin.InitialsOf(title) + " " + Pinyin.InitialsOf(string.Join(" ", tags))).ToLowerInvariant();
                return py.Contains(kw);
            }).ToList();
        }
        return Storage.SortClips(list);
    }

    // ---- 过期（对齐 clips-store.js isExpired / resolveExpire）----
    /// <summary>过期判断：expireAt 为 null/0 = 永久。</summary>
    public static bool IsExpired(ClipItem c)
    {
        return c.ExpireAt.HasValue && c.ExpireAt.Value < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>解析过期选项：'1h'|'1d'|'7d'|'30d'|''(永久) → 绝对时间戳；非法返回 null。</summary>
    public static long? ResolveExpire(string? opt)
    {
        if (string.IsNullOrEmpty(opt)) return null;
        var m = ExpireRe.Match(opt.Trim());
        if (!m.Success) return null;
        var n = long.Parse(m.Groups[1].Value);
        var unit = m.Groups[2].Value == "h" ? 3_600_000L : 86_400_000L;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + n * unit;
    }

    // ---- 净化（对齐 clips-store.js sanitizeInput / sanitizeHtml）----
    /// <summary>输入净化（标题/标签）：trim + 截长 + 去重 + 上限（写操作共用）。</summary>
    public static (string Title, List<string> Tags) SanitizeInput(string? title, List<string>? tags)
    {
        var t = Truncate((title ?? "").Trim(), MaxTitle);
        var list = new List<string>();
        if (tags != null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in tags)
            {
                var tag = Truncate((raw ?? "").Trim(), MaxTagLen);
                if (tag.Length == 0 || seen.Contains(tag)) continue; // 对齐 filter(Boolean) + Set 去重（首次出现保留）
                seen.Add(tag);
                list.Add(tag);
                if (list.Count >= MaxTags) break; // 对齐 slice(0, MAX_TAGS)
            }
        }
        return (t, list);
    }

    /// <summary>html 净化：空串原样；截长到 512KB（对齐 sanitizeHtml）。</summary>
    public static string SanitizeHtml(string? html)
    {
        var h = html ?? "";
        return h.Length == 0 ? "" : Truncate(h, MaxHtml);
    }

    private static string Truncate(string s, int max)
    {
        return s.Length > max ? s[..max] : s;
    }
}
