using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipboardExe;

/// <summary>
/// JSON 数据层——对齐 Web 版存储语义：
///  - 原子写（tmp + rename，不丢数据）
///  - 排序：星标优先 → 复制次数降序 → 最近更新（与 Web 版 sortClips 一致）
///  - Web 导出 JSON 导入（{app, version, exportedAt, clips[]}，同 id 取 updatedAt 新者）
///  - UUID 校验（非 UUID 的导入 id 重新生成，防后续编辑/删除错乱）
/// 数据文件：data/clips.json（单用户本地；Web 版是 users/&lt;uid&gt;.json，EXE 免去用户维度）
/// </summary>
public sealed class Storage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly Regex UuidRe = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

    private readonly string _clipsPath;
    private readonly string _filesDir;
    private readonly object _lock = new();

    public string FilesDir => _filesDir;

    public Storage(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _clipsPath = Path.Combine(dataDir, "clips.json");
        _filesDir = Path.Combine(dataDir, "files");
        Directory.CreateDirectory(_filesDir);
    }

    // ---------------- 读取 / 写入 ----------------

    /// <summary>读取全部条目（活跃 ∪ 归档）。文件缺失返回空；损坏则备份后返回空（不崩溃）。</summary>
    public List<ClipItem> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_clipsPath)) return new List<ClipItem>();
            try
            {
                var json = File.ReadAllText(_clipsPath);
                var list = JsonSerializer.Deserialize<List<ClipItem>>(json, JsonOpts) ?? new List<ClipItem>();
                return list;
            }
            catch (Exception ex)
            {
                // 数据损坏：备份原文件供人工排查，启动不崩溃
                var backup = _clipsPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(_clipsPath, backup); } catch { /* 备份失败不阻塞 */ }
                AppLog.Info($"clips.json 损坏已备份为 {Path.GetFileName(backup)}: {ex.Message}");
                return new List<ClipItem>();
            }
        }
    }

    /// <summary>原子写：先写临时文件再改名覆盖，中途崩溃不会留下半截数据。</summary>
    public void Save(List<ClipItem> list)
    {
        lock (_lock)
        {
            var tmp = _clipsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonOpts));
            File.Move(tmp, _clipsPath, overwrite: true);
        }
    }

    // ---------------- 增删改查 ----------------

    /// <summary>追加条目（已去重逻辑由调用方处理）。</summary>
    public void Add(ClipItem item)
    {
        var list = Load();
        list.Add(item);
        Save(list);
    }

    /// <summary>删除条目（含文件实体清理）。返回是否存在。</summary>
    public bool Delete(string id)
    {
        var list = Load();
        var removed = list.RemoveAll(c => c.Id == id);
        if (removed > 0)
        {
            Save(list);
            var clip = FindById(list, id);
            // 清理文件实体（图片）
            if (clip != null && !string.IsNullOrEmpty(clip.FileId))
            {
                var f = Path.Combine(_filesDir, clip.FileId);
                try { if (File.Exists(f)) File.Delete(f); } catch { /* 清理失败不阻塞 */ }
            }
        }
        return removed > 0;
    }

    /// <summary>删除单个文件实体（按 fileId，供外部清理）。</summary>
    public void DeleteFile(string fileId)
    {
        if (string.IsNullOrEmpty(fileId)) return;
        try
        {
            var f = Path.Combine(_filesDir, fileId);
            if (File.Exists(f)) File.Delete(f);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>保存图片实体到 data/files/，返回 fileId 文件名。</summary>
    public string SaveImage(byte[] bytes, string mime, string ext)
    {
        var fileId = Guid.NewGuid().ToString() + ext;
        var path = Path.Combine(_filesDir, fileId);
        File.WriteAllBytes(path, bytes);
        return fileId;
    }

    /// <summary>读取图片实体字节（不存在返回 null）。</summary>
    public byte[]? LoadImage(string fileId)
    {
        if (string.IsNullOrEmpty(fileId)) return null;
        var path = Path.Combine(_filesDir, fileId);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>排序：星标 → 复制次数降序 → 最近更新（Web 版 sortClips 同款）。</summary>
    public static List<ClipItem> Sort(List<ClipItem> list)
    {
        return list.OrderByDescending(c => c.Pinned)
                   .ThenByDescending(c => c.CopyCount)
                   .ThenByDescending(c => c.UpdatedAt)
                   .ToList();
    }

    // ---------------- Web 互导 ----------------

    /// <summary>导出为 Web 版格式（{app, version, exportedAt, clips[]}）。</summary>
    public string ExportJson()
    {
        var payload = new Dictionary<string, object?>
        {
            ["app"] = "clipboard",
            ["version"] = 1,
            ["exportedAt"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            ["clips"] = Load(),
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    /// <summary>导入 Web 导出 JSON：合并去重（同 id 取 updatedAt 新者）。返回导入/跳过计数。</summary>
    public (int imported, int skipped) ImportFromWeb(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("clips", out var clipsEl) || clipsEl.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("导入文件缺少 clips 数组（非 Web 版导出格式）");
        }

        var incoming = clipsEl.EnumerateArray()
            .Select(el => JsonSerializer.Deserialize<ClipItem>(el.GetRawText(), JsonOpts))
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        // 规范化：id 必须 UUID（非 UUID 重新生成，否则后续按 id 编辑/删除全失效）
        foreach (var c in incoming)
        {
            if (!UuidRe.IsMatch(c.Id)) c.Id = Guid.NewGuid().ToString();
            if (c.Tags == null) c.Tags = new List<string>();
        }

        var local = Load();
        var byId = new Dictionary<string, ClipItem>();
        foreach (var c in local) byId[c.Id] = c;

        int imported = 0, skipped = 0;
        foreach (var c in incoming)
        {
            if (byId.TryGetValue(c.Id, out var existing))
            {
                if (c.UpdatedAt > existing.UpdatedAt) { existing = c; byId[c.Id] = c; imported++; }
                else skipped++;
            }
            else
            {
                byId[c.Id] = c;
                imported++;
            }
        }
        Save(byId.Values.ToList());
        return (imported, skipped);
    }

    private static ClipItem? FindById(List<ClipItem> list, string id)
        => list.FirstOrDefault(c => c.Id == id);
}
