// Services/Storage.cs - 条目持久化（对齐 Web 版 lib/core/store.js writeJson + clips-store.js 文件语义）
// 单机形态（已确认）：data/clips.json 主数据 {app,version,clips[]}（与 Web 导出同构），
// data/archive.json 滚动归档（Web users/<id>.archive.json 的直接对应）。
// 原子写：临时文件 + rename（对齐 writeJson：file + ".tmp-" + pid），写一半崩溃不留坏文件。
using System.IO;
using System.Text.Json;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

public sealed class Storage
{
    /// <summary>活跃区上限（CONFIG.MAX_CLIPS_PER_USER = 500，超出滚动进归档）。</summary>
    public const int MaxClipsPerUser = 500;

    /// <summary>含归档搜索单次读取上限（CONFIG.ARCHIVE_SCAN_LIMIT = 5000）。</summary>
    public const int ArchiveScanLimit = 5000;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // 文档 {app,version,clips}；ClipItem 有显式 JsonPropertyName 不受影响
    };

    private readonly string _clipsFile;
    private readonly string _archiveFile;

    public Storage(string dataDir)
    {
        _clipsFile = Path.Combine(dataDir, "clips.json");
        _archiveFile = Path.Combine(dataDir, "archive.json");
    }

    public string ClipsFile => _clipsFile;
    public string ArchiveFile => _archiveFile;

    // ---- 读（readJson 容错：坏文件/不存在 → 默认空）----
    public List<ClipItem> LoadClips()
    {
        var doc = ReadJson<ClipsDoc>(_clipsFile);
        return doc?.Clips ?? new List<ClipItem>();
    }

    public List<ClipItem> LoadArchive()
    {
        var list = ReadJson<List<ClipItem>>(_archiveFile);
        return list ?? new List<ClipItem>();
    }

    // ---- 写 ----
    /// <summary>写活跃区；超上限时自动滚动最旧条目进归档（对齐 saveClips）。</summary>
    public void SaveClips(List<ClipItem> list)
    {
        var trimmed = RollToArchive(list);
        WriteJson(_clipsFile, new ClipsDoc
        {
            App = ClipboardExe.App.AppName,
            Version = ClipboardExe.App.AppVersion,
            Clips = trimmed,
        });
    }

    /// <summary>写归档数组（直接替换，不做滚动——对齐 saveArchive）。</summary>
    public void SaveArchive(List<ClipItem> list)
    {
        WriteJson(_archiveFile, list);
    }

    /// <summary>
    /// 滚动归档（对齐 rollToArchive）：活跃区超过 500 时保留"最近更新"的前 500 条
    /// （按 updatedAt 降序——刚存入/刚复制/刚编辑的条目绝不进归档），其余按 createdAt 升序追加进归档（零丢失）。
    /// 按 id 去重再追加，防同一批最旧条目重复滚入（v0.6.11 实测 800 条两次保存归档 300→600 翻倍膨胀）。
    /// 清空（空数组）不触发滚动。
    /// </summary>
    public List<ClipItem> RollToArchive(List<ClipItem> list)
    {
        if (list.Count <= MaxClipsPerUser) return list;
        // OrderByDescending 稳定（对齐 JS sort 稳定）：保持同级输入顺序
        var byRecent = list.OrderByDescending(c => c.UpdatedAt).ToList();
        var keep = byRecent.Take(MaxClipsPerUser).ToList();
        var overflow = byRecent.Skip(MaxClipsPerUser).OrderBy(c => c.CreatedAt).ToList();
        var arch = LoadArchive();
        var existing = new HashSet<string>(arch.Select(c => c.Id));
        foreach (var c in overflow)
        {
            if (!existing.Contains(c.Id)) { arch.Add(c); existing.Add(c.Id); }
        }
        SaveArchive(arch);
        return keep;
    }

    /// <summary>排序：星标优先 → 复制次数降序 → 最近更新（对齐 sortClips，LINQ 稳定链）。</summary>
    public static List<ClipItem> SortClips(IEnumerable<ClipItem> list)
    {
        return list
            .OrderByDescending(c => c.Pinned)
            .ThenByDescending(c => c.CopyCount)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();
    }

    /// <summary>原子写 JSON：先写临时文件再 rename（对齐 writeJson 语义）。</summary>
    public void WriteJson<T>(string file, T data)
    {
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = file + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json), new System.Text.UTF8Encoding(false)); // 无 BOM（对齐 writeFileSync utf8）
        File.Move(tmp, file, true); // true = 覆盖（rename 语义）
    }

    private T? ReadJson<T>(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
        }
        catch { return default; } // 坏文件不崩溃（对齐 readJson 容错）
    }

    /// <summary>data/clips.json 文档结构（对齐 Web 导出 {app,version,clips[]}）。</summary>
    private sealed class ClipsDoc
    {
        public string App { get; set; } = "";
        public string Version { get; set; } = "";
        public List<ClipItem> Clips { get; set; } = new();
    }
}
