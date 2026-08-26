// lib/routes/clips.js - 条目域路由：列表/搜索/标签 / 新增 / 编辑 / 删除 / 复制计数
import { sendJson, jsonBody } from "./helpers.js";
import * as clips from "../core/clips.js";
import { deleteFile } from "../core/files.js";
import { getSyncConfig } from "../core/webdav.js";
import { requireAuth } from "./helpers.js";

export const clipRoutes = [
  { p: "/api/clips", m: "GET", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const q = url.searchParams.get("q") || "";
    const tag = url.searchParams.get("tag") || "";
    const archived = url.searchParams.get("archived") === "1"; // 含归档查询（v0.2.0）
    sendJson(res, 200, { ok: true, clips: clips.listClips(userId, { q, tag, archived }) });
  } },
  { p: "/api/tags", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    sendJson(res, 200, { ok: true, tags: clips.listTags(userId) });
  } },
  // 标签管理（P1-6）：重命名 / 删除，跨活跃区+归档全部条目生效
  { p: "/api/tags/:name", m: "PUT", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const r = clips.renameTag(userId, url.params.name, body.name);
    sendJson(res, 200, r);
  } },
  { p: "/api/tags/:name", m: "DELETE", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const r = clips.deleteTag(userId, url.params.name);
    sendJson(res, 200, r);
  } },
  { p: "/api/clips", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const clip = clips.createClip(userId, body);
    sendJson(res, 201, { ok: true, clip });
  } },
  { p: "/api/clips/:id", m: "PUT", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const clip = clips.updateClip(userId, url.params.id, body);
    sendJson(res, 200, { ok: true, clip });
  } },
  { p: "/api/clips/:id", m: "DELETE", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    // v0.6.13：归档条目也可删除（WebDAV 完整备份后用户可手动清理用不到的归档）——活跃区没有则查归档
    let r;
    try {
      r = clips.deleteClip(userId, url.params.id);
    } catch (e) {
      if (e.status !== 404) throw e;
      r = clips.deleteArchivedClip(userId, url.params.id);
    }
    if (r.fileId) deleteFile(userId, r.fileId); // 联动清理文件实体
    // P-102：墓碑仅对「已配置 WebDAV 的用户」记录（未配置同步则无传播需求，不写文件）
    if (r.tombstone && getSyncConfig(userId)) clips.recordTombstone(userId, r.tombstone.id);
    sendJson(res, 200, r);
  } },
  // 手动归档（v0.6.13：编辑弹窗「归档」按钮）——活跃区移入归档区，非删除不记墓碑（归档仍参与 WebDAV 同步）
  { p: "/api/clips/:id/archive", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    sendJson(res, 200, clips.archiveClip(userId, url.params.id));
  } },
  // 恢复归档（v0.6.13：归档卡片「↺ 恢复」）——归档区移回活跃区，updatedAt 刷新防被滚动
  { p: "/api/clips/:id/restore", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    sendJson(res, 200, clips.unarchiveClip(userId, url.params.id));
  } },
  // 全部清空：不记墓碑（清空 = 想从网上同步，下次 WebDAV 同步从远端拉回恢复）
  { p: "/api/clips", m: "DELETE", handler: async (req, res) => {
    const userId = requireAuth(req);
    const r = clips.clearAllClips(userId);
    if (r.fileIds.length) {
      for (const fid of r.fileIds) deleteFile(userId, fid); // 逐个清理文件实体
    }
    sendJson(res, 200, r);
  } },
  { p: "/api/clips/:id/copy", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const r = clips.bumpCopy(userId, url.params.id);
    sendJson(res, 200, r);
  } },
  { p: "/api/clips/:id/pin", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const r = clips.togglePin(userId, url.params.id);
    sendJson(res, 200, r);
  } },
  // 导出 / 导入（本地文件备份，v0.2.0）
  { p: "/api/export", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    sendJson(res, 200, { ok: true, data: clips.exportClips(userId) });
  } },
  { p: "/api/import", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const payload = body && body.data ? body.data : body; // 兼容 { data } 包装与裸 { clips }
    const r = clips.importClips(userId, payload && payload.clips);
    sendJson(res, 200, r);
  } },
];
