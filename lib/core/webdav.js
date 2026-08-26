// lib/core/webdav.js - WebDAV 备份与同步
// 设计参考 edge-multi-account-cookie 的 WebDAV 方案，语义完全对齐：
//   1) 单独删除 → 记墓碑 → 同步传播删除（防旧备份把已删条目复活）
//   2) 全部清空 → 不记墓碑（清空即"想从网上同步"，下次同步从远端拉回恢复）
//   3) 双向取最新：同 id 条目按 updatedAt 取新者；墓碑 deletedAt > updatedAt → 删除，
//      另一侧删后又编辑（updatedAt > deletedAt）→ 保留
//   4) 本地无数据时跳过上传（防把清空后的空备份覆盖远端）
//   5) 远端固定保留最新 1 份快照：<目录>/workbuddy/剪贴板/clipboard-<uid>.json（v0.6.7 起统一子目录；
//      v0.6.13 起快照 = 活跃区 ∪ 归档区（归档条目带 archived 标记），WebDAV 完整备份）
//   6) 实体同步开关（syncFiles）：勾选 → 文件实体也备份/恢复（<目录>/workbuddy/剪贴板/files/<uid>/<fileId>.<ext>）；
//      不勾选 → 快照只含条目元数据（含 fileId 引用，恢复后文件实体缺失时下载 404）
//   7) 定时自动同步（autoSync + intervalMin）：服务进程存活期间按间隔自动双向同步，默认 12 小时
import fs from "node:fs";
import path from "node:path";
import { CONFIG, EXT_BY_MIME } from "./config.js";
import { readJson, writeJson, httpError } from "./store.js";
import { loadClips, saveClips, loadArchive, saveArchive, loadTombstones, saveTombstones, pruneTombstones } from "./clips.js";
import { getFilePath } from "./files.js";

const REQ_TIMEOUT_MS = 10_000; // WebDAV 请求超时（与前端 fetch 超时一致，防挂起）
const DEFAULT_INTERVAL_MIN = 720; // 自动同步默认间隔：12 小时
const AUTO_MIN = 30, AUTO_MAX = 24 * 60; // 间隔范围 30 分钟 ~ 24 小时

// v0.6.7：远端统一存放于「配置目录/workbuddy/剪贴板/」子目录下（与 WebDAV 根的其他用途隔离）
const REMOTE_SUBDIR = "workbuddy/剪贴板";

// ---------- 配置存储（每个用户独立） ----------

function cfgFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".webdav.json");
}

/** 读同步配置；未配置返回 null */
export function getSyncConfig(userId) {
  return readJson(cfgFile(userId), null);
}

function clampInt(v, min, max, def) {
  const n = parseInt(v, 10);
  if (isNaN(n)) return def;
  return Math.min(max, Math.max(min, n));
}

/** 保存配置（调用方先测试连通）；pass 留空 = 复用旧密码（前端密码框留空时）。
 *  syncFiles=是否同步文件实体；autoSync=是否定时自动同步；intervalMin=间隔分钟（默认 720=12h）。 */
export function saveSyncConfig(userId, { url, user, pass, syncFiles, autoSync, intervalMin }) {
  const old = getSyncConfig(userId) || {};
  const cfg = {
    url: String(url || "").trim(),
    user: String(user || "").trim(),
    pass: pass ? String(pass) : (old.pass || ""),
    syncFiles: !!syncFiles,
    autoSync: !!autoSync,
    intervalMin: clampInt(intervalMin, AUTO_MIN, AUTO_MAX, old.intervalMin || DEFAULT_INTERVAL_MIN),
    lastSyncAt: old.lastSyncAt || 0,
  };
  if (!cfg.url) throw httpError(400, "WebDAV 服务器地址不能为空");
  if (!/^https?:\/\//i.test(cfg.url)) throw httpError(400, "地址需以 http(s):// 开头");
  if (!cfg.pass) throw httpError(400, "密码不能为空（首次配置）");
  writeJson(cfgFile(userId), cfg);
  return { ok: true };
}

/** 局部更新配置（如同步后刷新 lastSyncAt，不触发校验） */
function updateConfig(userId, patch) {
  const cfg = getSyncConfig(userId);
  if (!cfg) return;
  writeJson(cfgFile(userId), { ...cfg, ...patch });
}

// ---------- WebDAV 客户端（Node fetch 直连，零依赖） ----------

function authHeader(cfg) {
  return "Basic " + Buffer.from(cfg.user + ":" + cfg.pass).toString("base64");
}

/** 目录 URL（去尾斜杠 + 加斜杠） */
function dirUrl(url) {
  return url.replace(/\/+$/, "") + "/";
}

/** 数据根目录 URL：配置目录 + workbuddy/剪贴板/（v0.6.7） */
function dataDirUrl(cfg) {
  return dirUrl(cfg.url) + REMOTE_SUBDIR.split("/").filter(Boolean).map(s => s + "/").join("");
}

/** 快照文件 URL */
function snapUrl(cfg, userId) {
  return dataDirUrl(cfg) + "clipboard-" + userId + ".json";
}

/** fetch 封装：Basic 认证 + 超时，返回 { status, buf, text }（buf 供二进制文件实体，text 供 JSON） */
async function davFetch(url, opts = {}) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), REQ_TIMEOUT_MS);
  try {
    const r = await fetch(url, { ...opts, signal: ctrl.signal });
    const buf = Buffer.from(await r.arrayBuffer().catch(() => new ArrayBuffer(0)));
    return { status: r.status, buf, text: buf.toString("utf8") };
  } catch (e) {
    const err = httpError(502, "WebDAV 连接失败: " + (e.name === "AbortError" ? "请求超时" : e.message));
    throw err;
  } finally {
    clearTimeout(timer);
  }
}

/** 确保目录存在（MKCOL；已存在/不支持均容忍）。v0.6.7：逐级创建「根 → workbuddy/ → workbuddy/剪贴板/」 */
async function ensureDir(cfg) {
  const segs = [dirUrl(cfg.url), ...REMOTE_SUBDIR.split("/").filter(Boolean).map(s => s + "/")];
  let cur = dirUrl(cfg.url);
  for (let i = 1; i < segs.length; i++) {
    cur = cur + segs[i];
    const r = await davFetch(cur, { method: "MKCOL", headers: { Authorization: authHeader(cfg) } });
    if (r.status === 401 || r.status === 403) throw httpError(401, "WebDAV 认证失败（检查用户名/密码）");
    if (![201, 204, 200, 301, 405].includes(r.status)) {
      throw httpError(502, "WebDAV 目录不可用（HTTP " + r.status + "）");
    }
  }
}

/** 连通测试：确保目录（含子目录）+ 写探针文件 + 读回校验。失败抛错（配置不保存） */
export async function testConnection(cfg) {
  await ensureDir(cfg);
  const probe = dataDirUrl(cfg) + ".clipboard-probe";
  const put = await davFetch(probe, {
    method: "PUT",
    headers: { Authorization: authHeader(cfg), "Content-Type": "text/plain" },
    body: "ok",
  });
  if (![201, 204, 200].includes(put.status)) {
    throw httpError(502, "WebDAV 不可写（PUT 返回 " + put.status + "）");
  }
  const get = await davFetch(probe, { headers: { Authorization: authHeader(cfg) } });
  if (get.status !== 200 || get.text !== "ok") {
    throw httpError(502, "WebDAV 读回校验失败");
  }
  return { ok: true };
}

/** 拉远端快照：404 → null；其他错误抛错 */
async function fetchRemoteSnapshot(cfg, userId) {
  const url = snapUrl(cfg, userId);
  const r = await davFetch(url, { headers: { Authorization: authHeader(cfg) } });
  if (r.status === 404) return null;
  if (r.status !== 200) throw httpError(502, "拉取远端备份失败（HTTP " + r.status + "）");
  try {
    const snap = JSON.parse(r.text);
    if (!snap || !Array.isArray(snap.clips)) throw new Error("格式错误");
    return snap;
  } catch {
    throw httpError(502, "远端备份文件损坏或格式不兼容");
  }
}

/** 上传快照 */
async function uploadSnapshot(cfg, userId, snap) {
  const url = snapUrl(cfg, userId);
  const r = await davFetch(url, {
    method: "PUT",
    headers: { Authorization: authHeader(cfg), "Content-Type": "application/json" },
    body: JSON.stringify(snap),
  });
  if (![201, 204, 200].includes(r.status)) {
    throw httpError(502, "上传远端备份失败（HTTP " + r.status + "）");
  }
}

// ---------- 合并算法（双向取最新 + 墓碑裁决） ----------

/**
 * 合并本地与远端快照：
 *  - 同 id 条目取 updatedAt 新者
 *  - 墓碑合并取 deletedAt 新者
 *  - 裁决：墓碑 deletedAt > 条目 updatedAt → 删除；条目 updatedAt > 墓碑 deletedAt → 保留（删后又被编辑）
 *  - 清空语义：localTomb=[] 表示"全部清空不传播删除"——此时远端条目直接拉回
 * @param {Array} localClips 本地条目
 * @param {Array} localTomb 本地墓碑 [{id,deletedAt}]
 * @param {Object|null} remoteSnap 远端快照 {clips, tombstones} 或 null
 * @returns {{clips:Array, tombstones:Array}}
 */
export function mergeSnapshots(localClips, localTomb, remoteSnap) {
  const byId = new Map();
  for (const c of localClips) byId.set(c.id, c);
  if (remoteSnap && Array.isArray(remoteSnap.clips)) {
    for (const c of remoteSnap.clips) {
      const ex = byId.get(c.id);
      if (!ex || (c.updatedAt || 0) > (ex.updatedAt || 0)) byId.set(c.id, c);
    }
  }
  const tombs = new Map();
  for (const t of localTomb || []) tombs.set(t.id, t.deletedAt);
  if (remoteSnap && Array.isArray(remoteSnap.tombstones)) {
    for (const t of remoteSnap.tombstones) {
      const ex = tombs.get(t.id);
      if (!ex || t.deletedAt > ex) tombs.set(t.id, t.deletedAt);
    }
  }
  const clips = [];
  for (const [id, c] of byId) {
    const delAt = tombs.get(id);
    if (delAt && delAt > (c.updatedAt || 0)) continue; // 墓碑裁决：删除
    clips.push(c);
  }
  const tombstones = [...tombs.entries()].map(([id, deletedAt]) => ({ id, deletedAt }));
  return { clips, tombstones };
}

// ---------- 实体同步（syncFiles 开关：勾选 → 文件实体也备份/恢复） ----------

/** 文件条目的远端/本地扩展名（mime 优先，其次原始文件名，兜底 .bin——与 saveFile 的 bin 兜底一致，
 *  v0.6.11 修复：此前兜底空串，无扩展名+未知 mime 的文件远端路径 <fileId>、本地存 <fileId>.bin，
 *  恢复写盘无扩展名 → getFilePath 前缀匹配失败 → 下载 404） */
function extFor(c) {
  const mime = EXT_BY_MIME[c.fileMime];
  if (mime) return "." + mime;
  const m = /\.([a-z0-9]{1,8})$/i.exec(c.fileName || "");
  return m ? "." + m[1].toLowerCase() : ".bin";
}

/**
 * 实体同步：对合并后的每个文件条目——
 *  - 本地有实体 → PUT 上传到 <目录>/files/<uid>/<fileId><ext>
 *  - 本地缺失（恢复场景）→ GET 远端写回本地 files 目录（远端也没有则跳过，条目保留）
 */
async function syncFileEntities(cfg, userId, clips) {
  const fBase = dataDirUrl(cfg) + "files/" + userId + "/"; // v0.6.7：子目录内 files/<uid>/
  for (const c of clips) {
    if (c.type !== "file" || !c.fileId) continue;
    const ext = extFor(c);
    const remote = fBase + c.fileId + ext;
    let local = null;
    try { local = getFilePath(userId, c.fileId); } catch { local = null; }
    if (local) {
      const buf = fs.readFileSync(local);
      const r = await davFetch(remote, {
        method: "PUT",
        headers: { Authorization: authHeader(cfg), "Content-Type": c.fileMime || "application/octet-stream" },
        body: buf,
      });
      if (![201, 204, 200].includes(r.status)) throw httpError(502, "文件实体上传失败：" + c.fileName);
    } else {
      const r = await davFetch(remote, { headers: { Authorization: authHeader(cfg) } });
      if (r.status === 200) {
        const dir = path.join(CONFIG.filesDir, userId);
        fs.mkdirSync(dir, { recursive: true });
        fs.writeFileSync(path.join(dir, c.fileId + ext), r.buf);
      } // 404 → 远端也无实体，跳过（条目保留，下载时提示）
    }
  }
}

// ---------- 一键同步（先拉远端合并进本地，再上传合并后全量） ----------

export async function runSync(userId) {
  const cfg = getSyncConfig(userId);
  if (!cfg) throw httpError(400, "未配置 WebDAV，请先测试保存");
  await ensureDir(cfg);

  const remoteSnap = await fetchRemoteSnapshot(cfg, userId); // 404 → null
  // v0.6.13：快照 = 活跃区 ∪ 归档区（归档条目带 archived 标记）——WebDAV 完整备份，归档不丢
  const localClips = [...loadClips(userId), ...loadArchive(userId).map((c) => ({ ...c, archived: true }))];
  const localTomb = pruneTombstones(userId); // 先清过期墓碑（防无限增长）

  const merged = mergeSnapshots(localClips, localTomb, remoteSnap);

  // 写回本地（拉回远端/合并结果）——按 archived 标记分拣：归档先替换（防止 saveClips 滚动追加覆盖），活跃再写（内部自动滚动）
  const active = merged.clips.filter((c) => !c.archived);
  const archived = merged.clips.filter((c) => c.archived);
  saveArchive(userId, archived);
  saveClips(userId, active);
  saveTombstones(userId, merged.tombstones);

  // 实体同步（勾选时）：本地有 → 上传；本地缺（恢复）→ 拉回
  if (cfg.syncFiles) await syncFileEntities(cfg, userId, merged.clips);

  // 上传保护：合并前本地无数据（全新设备 / 清空后想从网上同步）→ 跳过上传，防空备份覆盖远端。
  // 此时远端数据已拉回本地，下次同步本地非空即可正常双向收敛。
  const hadLocal = localClips.length > 0 || localTomb.length > 0;
  if (hadLocal) {
    await uploadSnapshot(cfg, userId, {
      app: "clipboard", version: 1,
      syncedAt: Date.now(),
      clips: merged.clips,
      tombstones: merged.tombstones,
    });
  }

  updateConfig(userId, { lastSyncAt: Date.now(), lastSyncError: "" }); // 成功即清失败标记（P-104）

  return {
    ok: true,
    remoteExisted: !!remoteSnap,
    uploaded: hadLocal,
    clips: merged.clips.length,
    tombstones: merged.tombstones.length,
  };
}

// ---------- 定时自动同步（服务进程存活期间按间隔执行；失败静默） ----------

const autoSyncInProgress = new Set(); // 防同一用户重入

export async function runAutoSync() {
  if (!fs.existsSync(CONFIG.usersDir)) return;
  for (const f of fs.readdirSync(CONFIG.usersDir)) {
    if (!f.endsWith(".webdav.json")) continue;
    const userId = f.slice(0, -".webdav.json".length);
    const cfg = getSyncConfig(userId);
    if (!cfg || !cfg.autoSync || autoSyncInProgress.has(userId)) continue;
    const due = (cfg.lastSyncAt || 0) + (cfg.intervalMin || DEFAULT_INTERVAL_MIN) * 60_000;
    if (Date.now() < due) continue;
  autoSyncInProgress.add(userId);
  try { await runSync(userId); }
  catch (e) {
    // P-104：自动同步失败不再静默——记 lastSyncError 供前端展示（不更新 lastSyncAt，保持原有"每周期重试"节奏）
    updateConfig(userId, { lastSyncError: (e && e.message) || "同步失败" });
  }
  finally { autoSyncInProgress.delete(userId); }
  }
}
