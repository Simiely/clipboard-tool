// lib/routes/users.js - 用户域路由：列表 / 新建 / 会话 / 改密 / 删除 / 登出
import { sendJson, jsonBody } from "./helpers.js";
import * as users from "../core/users.js";
import { requireAuth } from "./helpers.js";

export const userRoutes = [
  { p: "/api/users", m: "GET", handler: async (req, res) => {
    sendJson(res, 200, { ok: true, users: users.listUsers() });
  } },
  { p: "/api/users", m: "POST", handler: async (req, res) => {
    const body = await jsonBody(req);
    // v0.6.13 双名模型：body.name = 账号名（不可变身份键）；body.displayName = 显示名（可改，留空=账号名）
    const user = users.createUser(body.name, body.displayName, body.password || "");
    // 新建即签发 token（零摩擦进入）
    const token = users.createToken(user.id);
    sendJson(res, 201, { ok: true, user, token });
  } },
  { p: "/api/session", m: "POST", handler: async (req, res) => {
    const ip = req.socket.remoteAddress || "";
    const body = await jsonBody(req);
    const targetId = body.id || "";
    if (users.isLoginBlocked(ip, targetId)) {
      return sendJson(res, 429, { ok: false, error: "尝试过于频繁，请稍后再试" });
    }
    try {
      const r = users.login(body.id, body.password || "");
      users.clearLoginFail(ip, targetId);
      sendJson(res, 200, { ok: true, ...r });
    } catch (e) {
      // 登录失败即计数（401 密码错 / 404 用户不存在都防爆破；400 参数无效不计数）
      if (e.status === 401 || e.status === 404) users.noteLoginFail(ip, targetId);
      sendJson(res, e.status || 500, { ok: false, error: e.message });
    }
  } },
  { p: "/api/session", m: "DELETE", handler: async (req, res) => {
    const token = (req.headers["authorization"] || "").replace(/^Bearer\s+/i, "");
    users.destroyToken(token);
    sendJson(res, 200, { ok: true });
  } },
  { p: "/api/users/:id/password", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    if (userId !== url.params.id) return sendJson(res, 403, { ok: false, error: "无权修改他人密码" });
    const body = await jsonBody(req);
    const keepToken = (req.headers["authorization"] || "").replace(/^Bearer\s+/i, "");
    const r = users.changePassword(userId, body.oldPassword, body.newPassword, keepToken);
    sendJson(res, 200, r);
  } },
  // v0.6.13：修改用户名（数据管理入口；同名校验 409，不销毁会话）
  { p: "/api/users/:id/name", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    if (userId !== url.params.id) return sendJson(res, 403, { ok: false, error: "无权修改他人昵称" });
    const body = await jsonBody(req);
    sendJson(res, 200, users.renameUser(userId, body.name));
  } },
  // v0.6.13：取当前用户最新信息（boot 恢复登录刷新缓存——改名后强制刷新不回落旧值）
  { p: "/api/users/me", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    sendJson(res, 200, { ok: true, user: users.getUserPublic(userId) });
  } },
  { p: "/api/users/:id", m: "DELETE", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    if (userId !== url.params.id) return sendJson(res, 403, { ok: false, error: "无权删除他人账号" });
    const r = users.deleteUser(userId);
    sendJson(res, 200, r);
  } },
];
