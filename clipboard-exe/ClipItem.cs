using System.Text.Json.Serialization;

namespace ClipboardExe;

/// <summary>
/// 剪贴板条目模型——字段与 Web 版 lib/core/clips-store.js publicClip 完全对齐，
/// 保证 Web 导出 JSON 可直接导入（格式互导）。
/// </summary>
public sealed class ClipItem
{
    /// <summary>UUID（Web 版 ID_RE 校验；导入时非 UUID 重新生成）</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>类型：text | link | file（Web 版枚举；图片走 file + fileMime=image/*）</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>标题（可空；链接默认取 URL 前 60 字符）</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>纯文本内容（text 必填；搜索/排序/重复检测的键）</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>富文本 HTML（CF_HTML 片段，≤512KB；卡片分栏复制用）</summary>
    [JsonPropertyName("html")]
    public string Html { get; set; } = "";

    /// <summary>链接 URL（link 类型）</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>标签（点选已有 + 输入新建）</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>文件实体引用（file 类型；图片存 data/files/&lt;fileId&gt;）</summary>
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = "";

    /// <summary>原始文件名</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    /// <summary>文件字节数</summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>MIME（image/* 在卡片点击时复制为图片）</summary>
    [JsonPropertyName("fileMime")]
    public string FileMime { get; set; } = "";

    /// <summary>复制次数（点击卡片复制 +1；排序权重）</summary>
    [JsonPropertyName("copyCount")]
    public int CopyCount { get; set; }

    /// <summary>星标置顶（排序最高优先级）</summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }

    /// <summary>归档标记（Web 导出里活跃 ∪ 归档同数组，归档带此标记）</summary>
    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    /// <summary>过期时间（ms；保留字段，EXE 未启用清扫）</summary>
    [JsonPropertyName("expireAt")]
    public long? ExpireAt { get; set; }

    /// <summary>创建时间（ms，对齐 Web Date.now()）</summary>
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }

    /// <summary>更新时间（ms；合并去重的 key——同 id 取新者）</summary>
    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }
}
