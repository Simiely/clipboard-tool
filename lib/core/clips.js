// lib/core/clips.js - 条目域：CRUD + 复制计数 + 搜索/标签过滤 + 过期清扫
// 排序规则在本层单一实现（copyCount 降序 → updatedAt 降序），前端只渲染。
// 文件条目统一模型：type=file 时 fileId 指向 files 目录，主干 CRUD 只有一套。
import crypto from "node:crypto";
import path from "node:path";
import fs from "node:fs";
import { CONFIG } from "./config.js";
import { readJson, writeJson, httpError, assertId } from "./store.js";

function clipsFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".json");
}

function archiveFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".archive.json");
}

/** 读用户条目原始数组（同步模块也使用） */
export function loadClips(userId) {
  const list = readJson(clipsFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 读归档数组（滚动归档：活跃区超限后最旧条目移入，只读居多） */
export function loadArchive(userId) {
  const list = readJson(archiveFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 写用户条目数组（同步模块也使用）；超上限时自动滚动最旧条目进归档 */
export function saveClips(userId, list) {
  const trimmed = rollToArchive(userId, list);
  writeJson(clipsFile(userId), trimmed);
}

/**
 * 滚动归档：活跃区超过 MAX_CLIPS_PER_USER 时，保留"最近更新"的前 N 条
 * （按 updatedAt 降序——刚存入/刚复制/刚编辑的条目绝不进归档），
 * 其余按 createdAt 升序追加进归档（零丢失）。清空（空数组）不触发滚动。
 * 注意：不按 copyCount 排序——否则新条目（copyCount=0）会被当成低价值立即滚走（v0.3.1 修复）。
 */
function rollToArchive(userId, list) {
  const MAX = CONFIG.MAX_CLIPS_PER_USER;
  if (!MAX || list.length <= MAX) return list;
  const byRecent = [...list].sort((a, b) => (b.updatedAt || 0) - (a.updatedAt || 0));
  const keep = byRecent.slice(0, MAX);
  const overflow = byRecent.slice(MAX).sort((a, b) => (a.createdAt || 0) - (b.createdAt || 0));
  const arch = loadArchive(userId);
  arch.push(...overflow);
  writeJson(archiveFile(userId), arch);
  return keep;
}

// ---------- 墓碑（tombstone）：单独删除的传播记录（WebDAV 同步用） ----------
// 语义（与 edge-multi-account-cookie 的 WebDAV 设计一致）：
//  - 单独删除 → 记墓碑 { id, deletedAt } → 同步时传播删除（防旧备份把已删条目复活）
//  - 全部清空 → 不记墓碑（清空即"想从网上同步"，下次同步从远端拉回恢复）
// 墓碑独立文件 <uid>.tombstones.json，不改动既有条目文件格式（零迁移）。
const TOMB_TTL_MS = 90 * 24 * 3600 * 1000; // 墓碑保留 90 天（远小于"删后改"防复活的合理窗口）

function tombFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".tombstones.json");
}

/** 读墓碑（数组，容错） */
export function loadTombstones(userId) {
  const list = readJson(tombFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 写墓碑数组（同步模块也使用） */
export function saveTombstones(userId, list) {
  writeJson(tombFile(userId), list);
}

/** 记一条墓碑（删除条目时调用）；同 id 墓碑保留最新 deletedAt */
function recordTombstone(userId, clipId) {
  const list = loadTombstones(userId).filter((t) => t.id !== clipId);
  list.push({ id: clipId, deletedAt: Date.now() });
  saveTombstones(userId, list);
}

/** 清理过期墓碑（>90 天，防无限增长） */
export function pruneTombstones(userId) {
  const now = Date.now();
  const kept = loadTombstones(userId).filter((t) => now - t.deletedAt < TOMB_TTL_MS);
  saveTombstones(userId, kept);
  return kept;
}

/** 清空墓碑（全部清空时调用——清空不传播删除） */
function clearTombstones(userId) {
  writeJson(tombFile(userId), []);
}

/** 条目对外视图（前端渲染用） */
function publicClip(c) {
  return {
    id: c.id, type: c.type, title: c.title, content: c.content,
    html: c.html || "", url: c.url, tags: c.tags, fileId: c.fileId, fileName: c.fileName,
    fileSize: c.fileSize, fileMime: c.fileMime,
    copyCount: c.copyCount, pinned: !!c.pinned, archived: !!c.archived, expireAt: c.expireAt,
    createdAt: c.createdAt, updatedAt: c.updatedAt,
  };
}

/** html 字段净化：白名单字符串 + 长度上限（512KB）。富文本随条目存储，前端按钮二选一复制 */
function sanitizeHtml(raw) {
  const h = String(raw || "");
  if (!h) return "";
  return h.slice(0, CONFIG.MAX_HTML);
}

/** 排序：星标优先 → 复制次数降序 → 最近更新（单一实现，勿在前端重复） */
function sortClips(list) {
  return [...list].sort((a, b) => {
    if (!!b.pinned !== !!a.pinned) return (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0);
    if ((b.copyCount || 0) !== (a.copyCount || 0)) return (b.copyCount || 0) - (a.copyCount || 0);
    return (b.updatedAt || 0) - (a.updatedAt || 0);
  });
}

// ---------- 相似内容归拢（用户需求：内容共享 ≥10 字符片段 → 两条排在一起） ----------
const SIM_GRAM = 10;      // 相似阈值：共享 ≥10 字符片段
const SIM_MAX_LEN = 500;  // 参与比较的最大长度（防超长文本拖慢）
/**
 * 用 10 字符 ngram 倒排索引判定相似并归拢。
 * 性能：建索引 O(n×L)，查询 O(L×桶大小)——100 条内容毫秒级，避免两两全文比较的 O(n²·L³)。
 * 语义：两条内容共享任意一个 10 字符连续片段即视为相似（与"超过 10 个字符相同"一致）。
 */
function groupSimilar(list) {
  const index = new Map(); // 10-gram -> Set<条目下标>
  list.forEach((c, i) => {
    const text = (c.type === "link" ? c.url : c.content || "").slice(0, SIM_MAX_LEN);
    if (text.length < SIM_GRAM) return;
    for (let k = 0; k + SIM_GRAM <= text.length; k++) {
      const g = text.slice(k, k + SIM_GRAM);
      if (!index.has(g)) index.set(g, new Set());
      index.get(g).add(i);
    }
  });
  const used = new Set();
  const result = [];
  for (let i = 0; i < list.length; i++) {
    if (used.has(i)) continue;
    used.add(i);
    result.push(list[i]);
    const text = (list[i].type === "link" ? list[i].url : list[i].content || "").slice(0, SIM_MAX_LEN);
    const sim = new Set();
    for (let k = 0; k + SIM_GRAM <= text.length; k++) {
      const bucket = index.get(text.slice(k, k + SIM_GRAM));
      if (bucket) for (const j of bucket) if (j !== i && !used.has(j)) sim.add(j);
    }
    for (const j of [...sim].sort((a, b) => a - b)) { used.add(j); result.push(list[j]); }
  }
  return result;
}

/** 过期判断：expireAt 为 null/0 = 永久 */
function isExpired(c) {
  return !!c.expireAt && c.expireAt < Date.now();
}

/** 解析过期选项：'1h'|'1d'|'7d'|'30d'|''(永久) → 绝对时间戳 */
export function resolveExpire(opt) {
  if (!opt) return null;
  const m = /^(\d+)([hd])$/.exec(String(opt).trim());
  if (!m) return null;
  const n = parseInt(m[1], 10);
  const unit = m[2] === "h" ? 3_600_000 : 86_400_000;
  return Date.now() + n * unit;
}

// ---------- URL 自动清理（v0.2.0：去追踪参数，保持链接干净） ----------
/** 常见追踪参数（UTM + 渠道统计），保存链接时自动剔除 */
const TRACKING_KEYS = new Set([
  "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
  "utm_id", "utm_source_platform", "utm_creative_format", "utm_marketing_tactic",
  "fbclid", "gclid", "gclsrc", "dclid", "msclkid", "mc_cid", "mc_eid",
  "igshid", "ref", "spm", "scm", "from", "from_sources", "clicktime", "clickid",
]);

/** 清理 URL 追踪参数：保留协议/主机/路径/其余参数，只剔除 TRACKING_KEYS。
 * 无追踪参数时原样返回；无法解析（畸形 URL）也原样返回。 */
export function cleanUrl(url) {
  if (!/^https?:\/\//i.test(url)) return url;
  try {
    const u = new URL(url);
    if (!u.search) return url;
    const kept = [];
    let removed = 0;
    for (const [k, v] of u.searchParams) {
      if (TRACKING_KEYS.has(k.toLowerCase())) removed++;
      else kept.push([k, v]);
    }
    if (!removed) return url;
    u.search = "";
    for (const [k, v] of kept) u.searchParams.append(k, v);
    return u.toString();
  } catch {
    return url;
  }
}

// ---------- 查询 ----------

/**
 * 按标签归拢（第二排序）：共享任意标签的条目排在一起。
 * 纯 O(n×标签数) 线性——Map 倒排收集标签→条目，查询 O(1)。比内容相似检测更轻。
 */
function groupByTags(list) {
  const tagIdx = new Map(); // tag -> Set<下标>
  list.forEach((c, i) => {
    for (const t of c.tags || []) {
      if (!tagIdx.has(t)) tagIdx.set(t, new Set());
      tagIdx.get(t).add(i);
    }
  });
  const used = new Set();
  const result = [];
  for (let i = 0; i < list.length; i++) {
    if (used.has(i)) continue;
    used.add(i);
    result.push(list[i]);
    const sim = new Set();
    for (const t of list[i].tags || []) {
      const bucket = tagIdx.get(t);
      if (bucket) for (const j of bucket) if (!used.has(j)) sim.add(j);
    }
    for (const j of [...sim].sort((a, b) => a - b)) { used.add(j); result.push(list[j]); }
  }
  return result;
}

/** 列表：可选搜索词 q（标题/内容/URL/标签模糊）+ 标签 tag（精确过滤）+ 含归档 archived，自动过滤已过期。
 * 排序策略（用户确认）：① pinned 置顶 → ② copyCount 降序 → ③ 标签相近归拢 → ④ 内容相近归拢。
 * 全部线性级，读取时计算（155 条约 5ms），任何修改即时反映，无需持久化排序。
 * 归档条目标记 archived=true（前端只读展示，不参与编辑/删除）。 */
export function listClips(userId, { q = "", tag = "", archived = false } = {}) {
  const kw = String(q || "").trim().toLowerCase();
  const tg = String(tag || "").trim();
  let list = loadClips(userId);
  if (archived) {
    // 归档合并：只取最近 ARCHIVE_SCAN_LIMIT 条（防超大归档拖慢），标记 archived 供前端只读
    const arch = loadArchive(userId).slice(-CONFIG.ARCHIVE_SCAN_LIMIT);
    list = [...arch.map((c) => ({ ...c, archived: true })), ...list];
  }
  list = list.filter((c) => !isExpired(c));
  if (kw) {
    list = list.filter((c) =>
      (c.title || "").toLowerCase().includes(kw) ||
      (c.content || "").toLowerCase().includes(kw) ||
      (c.url || "").toLowerCase().includes(kw) ||
      (c.tags || []).some((t) => t.toLowerCase().includes(kw))
    );
  }
  if (tg) list = list.filter((c) => (c.tags || []).includes(tg));
  return groupSimilar(groupByTags(sortClips(list))).map(publicClip);
}

/** 标签统计（前端标签过滤条） */
export function listTags(userId) {
  const counts = {};
  for (const c of loadClips(userId)) {
    if (isExpired(c)) continue;
    for (const t of c.tags || []) counts[t] = (counts[t] || 0) + 1;
  }
  return Object.entries(counts).map(([tag, count]) => ({ tag, count })).sort((a, b) => b.count - a.count);
}

/** 标签重命名：跨活跃区+归档全部条目替换（重名目标合并去重），返回受影响条目数 */
export function renameTag(userId, oldTag, newTag) {
  const from = String(oldTag || "").trim();
  const to = String(newTag || "").trim().slice(0, CONFIG.MAX_TAG_LEN);
  if (!from || !to) throw httpError(400, "标签名无效"); // v0.3.1：目标为空会静默删除标签，必须拒绝
  if (from === to) throw httpError(400, "标签名无效");
  let affected = 0;
  for (const [list, save] of [
    [loadClips(userId), (l) => writeJson(clipsFile(userId), l)],
    [loadArchive(userId), (l) => writeJson(archiveFile(userId), l)],
  ]) {
    for (const c of list) {
      if ((c.tags || []).includes(from)) {
        c.tags = [...new Set(c.tags.map((t) => (t === from ? to : t)).filter(Boolean))];
        affected++;
      }
    }
    save(list);
  }
  if (!affected) throw httpError(404, "标签不存在");
  return { ok: true, affected };
}

/** 标签删除：从活跃区+归档全部条目移除，返回受影响条目数 */
export function deleteTag(userId, tag) {
  const name = String(tag || "").trim();
  if (!name) throw httpError(400, "标签名无效");
  let affected = 0;
  for (const [list, save] of [
    [loadClips(userId), (l) => writeJson(clipsFile(userId), l)],
    [loadArchive(userId), (l) => writeJson(archiveFile(userId), l)],
  ]) {
    for (const c of list) {
      if ((c.tags || []).includes(name)) {
        c.tags = (c.tags || []).filter((t) => t !== name);
        affected++;
      }
    }
    save(list);
  }
  if (!affected) throw httpError(404, "标签不存在");
  return { ok: true, affected };
}

// ---------- 写操作 ----------

function sanitizeInput(body) {
  const title = String(body.title || "").trim().slice(0, CONFIG.MAX_TITLE);
  const tags = Array.isArray(body.tags)
    ? [...new Set(body.tags.map((t) => String(t).trim().slice(0, CONFIG.MAX_TAG_LEN)).filter(Boolean))].slice(0, CONFIG.MAX_TAGS)
    : [];
  return { title, tags };
}

/** 新增：type = text（content 必填）| link（url 必填）| file（fileId 必填） */
export function createClip(userId, body) {
  const { title, tags } = sanitizeInput(body);
  const type = ["text", "link", "file"].includes(body.type) ? body.type : "text";
  const clip = {
    id: crypto.randomUUID(),
    type,
    title,
    content: "",
    html: sanitizeHtml(body.html), // 富文本（可选；与 content 并存，前端双按钮复制）
    url: "",
    tags,
    fileId: "", fileName: "", fileSize: 0, fileMime: "",
    copyCount: 0,
    pinned: false,
    expireAt: resolveExpire(body.expire) || null,
    createdAt: Date.now(),
    updatedAt: Date.now(),
  };
  if (type === "text") {
    const content = String(body.content || "");
    if (!content) throw httpError(400, "内容不能为空");
    if (content.length > CONFIG.MAX_CONTENT) throw httpError(400, "内容过长");
    clip.content = content;
  } else if (type === "link") {
    const url = String(body.url || "");
    if (!/^https?:\/\/\S+$/i.test(url)) throw httpError(400, "链接需以 http(s):// 开头");
    clip.url = cleanUrl(url); // v0.2.0：自动去追踪参数（UTM/fbclid 等）
    if (!title) clip.title = clip.url.slice(0, 60); // v0.3.1：用清理后的 url 做标题，避免 utm 残留
  } else {
    if (!body.fileId || typeof body.fileId !== "string") throw httpError(400, "缺少文件");
    clip.fileId = body.fileId;
    clip.fileName = String(body.fileName || "file").slice(0, 255);
    clip.fileSize = Number(body.fileSize) || 0;
    clip.fileMime = String(body.fileMime || "");
  }
  const list = loadClips(userId);
  list.push(clip);
  saveClips(userId, list);
  return publicClip(clip);
}

/** 编辑：可改标题/内容/链接/标签/过期；文件条目不可换文件（先删再建） */
export function updateClip(userId, id, body) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const clip = list.find((c) => c.id === clipId);
  if (!clip) throw httpError(404, "条目不存在");
  const { title, tags } = sanitizeInput(body);
  if ("title" in body) clip.title = title;
  if ("tags" in body) clip.tags = tags;
  if ("expire" in body) clip.expireAt = resolveExpire(body.expire);
  if (clip.type === "text" && "content" in body) {
    const content = String(body.content || "");
    if (!content) throw httpError(400, "内容不能为空");
    if (content.length > CONFIG.MAX_CONTENT) throw httpError(400, "内容过长");
    clip.content = content;
  }
  if ("html" in body) clip.html = sanitizeHtml(body.html); // 富文本可独立更新（清空传空串）
  if (clip.type === "link" && "url" in body) {
    const url = String(body.url || "");
    if (!/^https?:\/\/\S+$/i.test(url)) throw httpError(400, "链接需以 http(s):// 开头");
    clip.url = cleanUrl(url); // v0.2.0：自动去追踪参数
  }
  clip.updatedAt = Date.now();
  saveClips(userId, list);
  return publicClip(clip);
}

/** 删除：返回是否删除（文件实体清理由调用方通过 fileId 处理，路由层联动 files 模块）。
 *  记墓碑——删除会在下次 WebDAV 同步时传播到其他设备（防旧备份复活）。 */
export function deleteClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const idx = list.findIndex((c) => c.id === clipId);
  if (idx < 0) throw httpError(404, "条目不存在");
  const [removed] = list.splice(idx, 1);
  saveClips(userId, list);
  recordTombstone(userId, clipId);
  return { ok: true, fileId: removed.type === "file" ? removed.fileId : null };
}

/** 全部清空：删除该用户所有条目（含归档）+ 清空墓碑（清空 = 想从网上同步，不传播删除）。
 *  返回需清理的文件实体 id 列表（路由层联动 files 模块）。 */
export function clearAllClips(userId) {
  const list = loadClips(userId);
  const fileIds = list.filter((c) => c.type === "file" && c.fileId).map((c) => c.fileId);
  writeJson(clipsFile(userId), []);
  writeJson(archiveFile(userId), []); // 归档一并清空（清空 = 全部不要）
  clearTombstones(userId);
  return { ok: true, cleared: list.length, fileIds };
}

/** 复制计数 +1（一键复制的统计，驱动排序）。过期条目与已删除同等对待（第三轮 F-3：与 getClip 一致） */
export function bumpCopy(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const clip = list.find((c) => c.id === clipId);
  if (!clip || isExpired(clip)) throw httpError(404, "条目不存在");
  clip.copyCount = (clip.copyCount || 0) + 1;
  clip.updatedAt = Date.now();
  saveClips(userId, list);
  return { ok: true, copyCount: clip.copyCount };
}

/** 星标切换：pinned 置顶优先于复制计数排序。过期条目等同不存在 */
export function togglePin(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const clip = list.find((c) => c.id === clipId);
  if (!clip || isExpired(clip)) throw httpError(404, "条目不存在");
  clip.pinned = !clip.pinned;
  clip.updatedAt = Date.now();
  saveClips(userId, list);
  return { ok: true, pinned: clip.pinned };
}

/** 取单条目（文件下载/打开前校验归属） */
export function getClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const clip = loadClips(userId).find((c) => c.id === clipId);
  if (!clip || isExpired(clip)) throw httpError(404, "条目不存在");
  return clip;
}

// ---------- 导出 / 导入（本地文件备份，v0.2.0） ----------

/** 导出：全量条目（活跃区 + 归档区，含文件元数据），供下载备份。 */
export function exportClips(userId) {
  return {
    app: "clipboard", version: 1, exportedAt: Date.now(),
    clips: [...loadClips(userId), ...loadArchive(userId)],
  };
}

/** 导入：合并去重（同 id 保留 updatedAt 新者），返回导入/跳过计数。
 * 不清理本地已有数据（合并语义）；文件实体引用保留但文件本体不迁移（下载时缺失会 404 提示）。 */
export function importClips(userId, items) {
  if (!Array.isArray(items)) throw httpError(400, "导入数据格式错误");
  if (items.length > CONFIG.MAX_CLIPS_PER_USER * 2) throw httpError(413, "导入条目过多");
  const byId = new Map();
  for (const c of loadClips(userId)) byId.set(c.id, c);
  let added = 0, updated = 0, skipped = 0;
  for (const raw of items) {
    if (!raw || typeof raw !== "object" || !raw.id) { skipped++; continue; }
    const clip = sanitizeImported(raw);
    const ex = byId.get(clip.id);
    if (ex) { // 同 id：取 updatedAt 新者（更新计数单独统计）
      if ((clip.updatedAt || 0) > (ex.updatedAt || 0)) { byId.set(clip.id, clip); updated++; }
      else skipped++;
    } else { byId.set(clip.id, clip); added++; }
  }
  const merged = [...byId.values()];
  const trimmed = rollToArchive(userId, merged); // 超限滚动进归档，不丢
  writeJson(clipsFile(userId), trimmed);
  return { ok: true, added, updated, skipped, total: merged.length };
}

/** 导入条目净化：只保留合法字段、校验类型/长度，防恶意 JSON 注入 */
function sanitizeImported(raw) {
  const type = ["text", "link", "file"].includes(raw.type) ? raw.type : "text";
  const clip = {
    id: String(raw.id).slice(0, 64),
    type,
    title: String(raw.title || "").slice(0, CONFIG.MAX_TITLE),
    content: String(raw.content || "").slice(0, CONFIG.MAX_CONTENT),
    html: sanitizeHtml(raw.html), // 富文本随导入迁移（超 512KB 截断）
    url: "",
    tags: Array.isArray(raw.tags)
      ? [...new Set(raw.tags.map((t) => String(t).slice(0, CONFIG.MAX_TAG_LEN)))].slice(0, CONFIG.MAX_TAGS)
      : [],
    fileId: "", fileName: "", fileSize: 0, fileMime: "",
    copyCount: Math.max(0, Math.floor(Number(raw.copyCount) || 0)),
    pinned: !!raw.pinned,
    expireAt: Number(raw.expireAt) || null,
    createdAt: Number(raw.createdAt) || Date.now(),
    updatedAt: Number(raw.updatedAt) || Date.now(),
  };
  if (type === "link") {
    const url = String(raw.url || "");
    if (/^https?:\/\/\S+$/i.test(url)) clip.url = url;
  } else if (type === "file" && typeof raw.fileId === "string") {
    clip.fileId = raw.fileId;
    clip.fileName = String(raw.fileName || "file").slice(0, 255);
    clip.fileSize = Math.max(0, Number(raw.fileSize) || 0);
    clip.fileMime = String(raw.fileMime || "");
  }
  return clip;
}

/** 后台过期清扫：删除过期条目（活跃区 + 归档区）并返回需清理的文件 id 列表（60s 周期，路由启动时挂载）。
 * 只处理 <uid>.json（活跃）与 <uid>.archive.json（归档）；.tombstones/.webdav 等附属文件不在此列。 */
export function sweepExpired() {
  const filesToDelete = [];
  if (!fs.existsSync(CONFIG.usersDir)) return filesToDelete;
  for (const f of fs.readdirSync(CONFIG.usersDir)) {
    if (!f.endsWith(".json")) continue;
    const isArchive = f.endsWith(".archive.json");
    const suffixLen = isArchive ? ".archive.json".length : ".json".length;
    const userId = f.slice(0, -suffixLen);
    if (!userId || /[.]/.test(userId)) continue; // 跳过 tombstones/webdav 等带后缀文件
    const list = isArchive ? loadArchive(userId) : loadClips(userId);
    const before = list.length;
    const kept = [];
    for (const c of list) {
      if (isExpired(c)) {
        if (c.type === "file" && c.fileId) filesToDelete.push({ userId, fileId: c.fileId });
      } else kept.push(c);
    }
    if (kept.length !== before) {
      if (isArchive) writeJson(archiveFile(userId), kept);
      else saveClips(userId, kept);
    }
  }
  return filesToDelete;
}
