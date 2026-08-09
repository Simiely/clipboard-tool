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

/** 读用户条目原始数组（同步模块也使用） */
export function loadClips(userId) {
  const list = readJson(clipsFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 写用户条目数组（同步模块也使用） */
export function saveClips(userId, list) {
  writeJson(clipsFile(userId), list);
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
    url: c.url, tags: c.tags, fileId: c.fileId, fileName: c.fileName,
    fileSize: c.fileSize, fileMime: c.fileMime,
    copyCount: c.copyCount, expireAt: c.expireAt,
    createdAt: c.createdAt, updatedAt: c.updatedAt,
  };
}

/** 排序：复制次数降序优先，其次最近更新（单一实现，勿在前端重复） */
function sortClips(list) {
  return [...list].sort((a, b) => {
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

/** 列表：可选搜索词 q（标题/内容/URL/标签模糊）+ 标签 tag（精确过滤），自动过滤已过期。
 * 排序策略（用户确认）：① copyCount 降序 → ② 标签相近归拢 → ③ 内容相近归拢。
 * 全部线性级，读取时计算（155 条约 5ms），任何修改即时反映，无需持久化排序。 */
export function listClips(userId, { q = "", tag = "" } = {}) {
  const kw = String(q || "").trim().toLowerCase();
  const tg = String(tag || "").trim();
  let list = loadClips(userId).filter((c) => !isExpired(c));
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
    url: "",
    tags,
    fileId: "", fileName: "", fileSize: 0, fileMime: "",
    copyCount: 0,
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
    clip.url = url;
    if (!title) clip.title = url.slice(0, 60);
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
  if (clip.type === "link" && "url" in body) {
    const url = String(body.url || "");
    if (!/^https?:\/\/\S+$/i.test(url)) throw httpError(400, "链接需以 http(s):// 开头");
    clip.url = url;
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

/** 全部清空：删除该用户所有条目 + 清空墓碑（清空 = 想从网上同步，不传播删除）。
 *  返回需清理的文件实体 id 列表（路由层联动 files 模块）。 */
export function clearAllClips(userId) {
  const list = loadClips(userId);
  const fileIds = list.filter((c) => c.type === "file" && c.fileId).map((c) => c.fileId);
  writeJson(clipsFile(userId), []);
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

/** 取单条目（文件下载/打开前校验归属） */
export function getClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const clip = loadClips(userId).find((c) => c.id === clipId);
  if (!clip || isExpired(clip)) throw httpError(404, "条目不存在");
  return clip;
}

/** 后台过期清扫：删除过期条目并返回需清理的文件 id 列表（60s 周期，路由启动时挂载） */
export function sweepExpired() {
  const filesToDelete = [];
  if (!fs.existsSync(CONFIG.usersDir)) return filesToDelete;
  for (const f of fs.readdirSync(CONFIG.usersDir)) {
    if (!f.endsWith(".json")) continue;
    const userId = f.slice(0, -5);
    const list = loadClips(userId);
    const before = list.length;
    const kept = [];
    for (const c of list) {
      if (isExpired(c)) {
        if (c.type === "file" && c.fileId) filesToDelete.push({ userId, fileId: c.fileId });
      } else kept.push(c);
    }
    if (kept.length !== before) saveClips(userId, kept);
  }
  return filesToDelete;
}
