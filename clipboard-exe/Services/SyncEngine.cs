// Services/SyncEngine.cs - 一键同步编排（逐行对齐 Web 版 lib/core/webdav.js runSync）
// 桌面单机形态（已确认）：单账号，配置存 data/webdav.json，账号名（accountName）为不可变身份键。
// 流程：ensureDir → 拉远端快照 → 合并（mergeSnapshots）→ 写回本地（按 archived 分拣）→（可选）实体同步
//       →（本地非空时）上传合并结果 → 刷新 lastSyncAt。重入保护 + 失败记 lastSyncError（对齐 runAutoSync 失败静默记错）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

/// <summary>同步结果（对齐 Web runSync 返回值 + 桌面需要的错误信息）。</summary>
public sealed class SyncResult
{
    public bool Ok;
    public bool RemoteExisted;
    public bool Uploaded;
    public int Clips;
    public int Tombstones;
    public string? Error;
}

public static class SyncEngine
{
    // v0.6.13：per-account 同步进行中——手动同步与定时 autoSync 可能并发，重入抛"同步进行中"（对齐 syncInFlight）。
    private static readonly HashSet<string> InFlight = new(StringComparer.Ordinal);
    // v0.6.14：墓碑过期清理（防无限增长，对齐 pruneTombstones 的 30 天窗口）。
    private const long TombstoneExpireMs = 30L * 24 * 3600 * 1000;

    /// <summary>是否到点需要自动同步（对齐 runAutoSync 的 due 判定）。</summary>
    public static bool IsDue(SyncConfig cfg)
    {
        if (!cfg.AutoSync) return false;
        var due = cfg.LastSyncAt + (long)cfg.IntervalMin * 60_000L;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= due;
    }

    /// <summary>WebDAV 一键同步（对齐 runSync）。dataDir 用于写回 webdav.json 的 lastSyncAt/lastSyncError。</summary>
    public static async Task<SyncResult> RunSync(Storage storage, FileStore fileStore, SyncConfig cfg, string dataDir)
    {
        if (!InFlight.Add(cfg.AccountName)) return Err("同步进行中，请稍候");
        try
        {
            await WebDavClient.EnsureDir(cfg);
            var name = cfg.AccountName;
            var curUrl = WebDavClient.SnapUrlFor(cfg, name);

            // ① 拉远端快照（桌面无旧格式 userId 路径 / 无改名迁移，仅需当前账号名快照）
            var remoteSnap = await WebDavClient.FetchRemoteSnapshot(cfg, curUrl);

            // ② 本地快照 = 活跃区 ∪ 归档区（归档条目带 archived 标记，对齐 runSync 的 localClips 组装）
            var active = storage.LoadClips();
            var archived = storage.LoadArchive().Select(c => { c.Archived = true; return c; }).ToList();
            var localClips = active.Concat(archived).ToList();
            // 先清过期墓碑（防无限增长）
            var localTomb = storage.LoadTombstones()
                .Where(t => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t.DeletedAt < TombstoneExpireMs)
                .ToList();

            // ③ 合并（双向取最新 + 墓碑裁决）
            var merged = WebDavSync.MergeSnapshots(localClips, localTomb, remoteSnap);

            // ④ 写回本地：归档先替换（防 saveClips 滚动覆盖），活跃再写（内部自动滚动）
            storage.SaveArchive(merged.Clips.Where(c => c.Archived).ToList());
            storage.SaveClips(merged.Clips.Where(c => !c.Archived).ToList());
            storage.SaveTombstones(merged.Tombstones);

            // ⑤ 实体同步（勾选时）
            if (cfg.SyncFiles) await SyncFileEntities(cfg, name, fileStore, merged.Clips);

            // ⑥ 上传保护：合并前本地无数据 → 跳过上传，防空备份覆盖远端（对齐 runSync hadLocal 判定）
            var hadLocal = localClips.Count > 0 || localTomb.Count > 0;
            if (hadLocal)
            {
                await WebDavClient.UploadSnapshot(cfg, curUrl, new Snapshot
                {
                    App = "clipboard",
                    Version = 1,
                    SyncedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Clips = merged.Clips,
                    Tombstones = merged.Tombstones,
                });
            }

            // ⑦ 收尾：刷新 lastSyncAt + 清错误标记（对齐 updateConfig）
            cfg.LastSyncAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            cfg.LastSyncError = "";
            WebDavSync.SaveConfig(dataDir, cfg);

            return new SyncResult
            {
                Ok = true,
                RemoteExisted = remoteSnap != null,
                Uploaded = hadLocal,
                Clips = merged.Clips.Count,
                Tombstones = merged.Tombstones.Count,
            };
        }
        catch (Exception ex)
        {
            try { cfg.LastSyncError = ex.Message; WebDavSync.SaveConfig(dataDir, cfg); } catch { /* 写回失败不影响返回 */ }
            return Err(ex.Message);
        }
        finally { InFlight.Remove(cfg.AccountName); }
    }

    /// <summary>实体同步（对齐 webdav.js syncFileEntities）：本地有实体 → PUT 上传；本地缺失（恢复）→ GET 拉回本地。</summary>
    private static async Task SyncFileEntities(SyncConfig cfg, string name, FileStore fileStore, List<ClipItem> clips)
    {
        var fBase = WebDavClient.FilesDirUrlFor(cfg, name);
        await WebDavClient.EnsureOneDir(cfg, WebDavClient.FilesRootUrlFor(cfg)); // files/
        await WebDavClient.EnsureOneDir(cfg, fBase); // files/<账号名>/
        foreach (var c in clips)
        {
            if (c.Type != "file" || string.IsNullOrEmpty(c.FileId)) continue;
            var ext = FileStore.ExtFor(c.FileMime, c.FileName);
            var remote = fBase + c.FileId + ext;
            byte[]? local = null;
            try { local = fileStore.ReadAllBytes(c.FileId); } catch { local = null; }
            if (local != null)
            {
                await WebDavClient.UploadFile(cfg, remote, local, c.FileMime ?? "application/octet-stream");
            }
            else
            {
                var got = await WebDavClient.DownloadFile(cfg, remote);
                if (got != null) fileStore.WriteRaw(c.FileId, ext, got); // 404 → 远端也无实体，跳过
            }
        }
    }

    private static SyncResult Err(string msg) => new() { Ok = false, Error = msg };
}
