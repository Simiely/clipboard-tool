// Models/SyncConfig.cs - WebDAV 同步配置 + 远端快照模型（对齐 Web 版 lib/core/webdav.js）
// 单机形态：配置单份，存 data/webdav.json；账号名（accountName）为不可变身份键，决定远端快照寻址。
using System.Text.Json.Serialization;

namespace ClipboardExe.Models;

/// <summary>WebDAV 同步配置（对齐 webdav.js saveSyncConfig 字段）。</summary>
public sealed class SyncConfig
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("user")] public string User { get; set; } = "";
    [JsonPropertyName("pass")] public string Pass { get; set; } = "";
    [JsonPropertyName("syncFiles")] public bool SyncFiles { get; set; }
    [JsonPropertyName("autoSync")] public bool AutoSync { get; set; }
    [JsonPropertyName("intervalMin")] public int IntervalMin { get; set; }
    [JsonPropertyName("lastSyncAt")] public long LastSyncAt { get; set; }
    [JsonPropertyName("lastSyncError")] public string LastSyncError { get; set; } = "";
    [JsonPropertyName("accountName")] public string AccountName { get; set; } = "default";
    [JsonPropertyName("pendingNameMigrations")]
    public List<string> PendingNameMigrations { get; set; } = new();
}

/// <summary>远端快照（对齐 webdav.js runSync 上传格式 {app,version,syncedAt,clips,tombstones}）。</summary>
public sealed class Snapshot
{
    [JsonPropertyName("app")] public string App { get; set; } = "clipboard";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("syncedAt")] public long SyncedAt { get; set; }
    [JsonPropertyName("clips")] public List<ClipItem> Clips { get; set; } = new();
    [JsonPropertyName("tombstones")] public List<Tombstone> Tombstones { get; set; } = new();
}
