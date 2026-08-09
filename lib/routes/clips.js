// lib/routes/clips.js - 条目域路由：列表/搜索/标签 / 新增 / 编辑 / 删除 / 复制计数
import { sendJson, jsonBody } from "./helpers.js";
import * as clips from "../core/clips.js";
import { deleteFile } from "../core/files.js";
import { requireAuth } from "./helpers.js";

export const clipRoutes = [
  { p: "/api/clips", m: "GET", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const q = url.searchParams.get("q") || "";
    const tag = url.searchParams.get("tag") || "";
    sendJson(res, 200, { ok: true, clips: clips.listClips(userId, { q, tag }) });
  } },
  { p: "/api/tags", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    sendJson(res, 200, { ok: true, tags: clips.listTags(userId) });
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
    const r = clips.deleteClip(userId, url.params.id);
    if (r.fileId) deleteFile(userId, r.fileId); // 联动清理文件实体
    sendJson(res, 200, r);
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
];
