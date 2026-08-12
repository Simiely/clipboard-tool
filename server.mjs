// server.mjs - 剪贴板工具入口（薄层）：HTTP 装配 + 路由分发 + 静态服务 + 过期清扫
// 平台托管时端口从 argv[2] 传入；数据目录由平台注入 CAP_STORAGE_DIR。
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { CONFIG } from "./lib/core/config.js";
import { matchRoute, withParams } from "./lib/routes/index.js";
import { sweepExpired } from "./lib/core/clips.js";
import { deleteFile } from "./lib/core/files.js";
import { runAutoSync } from "./lib/core/webdav.js";
import { pruneExpiredSessions } from "./lib/core/users.js";
import { sendJson } from "./lib/routes/helpers.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const INDEX_HTML = path.join(__dirname, "public", "index.html");
const APP_JS = path.join(__dirname, "public", "app.js"); // v0.4.0：前端 JS 拆独立文件

// 后台过期清扫：60s 周期，删除过期条目并联动清理文件实体
setInterval(() => {
  try {
    for (const { userId, fileId } of sweepExpired()) deleteFile(userId, fileId);
  } catch { /* 清扫失败不影响服务 */ }
}, CONFIG.SWEEP_INTERVAL_MS);

// 后台会话过期清理：60s 周期，惰性清理过期 token（v0.3.0 会话持久化）
setInterval(() => {
  try { pruneExpiredSessions(); } catch { /* 清理失败不影响服务 */ }
}, CONFIG.SWEEP_INTERVAL_MS);

// 后台定时自动同步：60s 周期，检查所有启用自动同步的用户是否到期（间隔各自配置，默认 12h）
setInterval(() => {
  try { runAutoSync(); } catch { /* 自动同步失败不影响服务 */ }
}, 60_000);

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://127.0.0.1");
  try {
    // 健康检查（平台托管要求）
    if (url.pathname === "/health") {
      return sendJson(res, 200, { ok: true, name: "clipboard" });
    }
    // 静态页面
    if (url.pathname === "/" || url.pathname === "/index.html") {
      res.writeHead(200, {
        "Content-Type": "text/html; charset=utf-8",
        "X-Content-Type-Options": "nosniff",
        "X-Frame-Options": "DENY",
      });
      return res.end(fs.readFileSync(INDEX_HTML));
    }
    // 前端 JS（v0.4.0 拆分独立文件）
    if (url.pathname === "/app.js") {
      res.writeHead(200, {
        "Content-Type": "text/javascript; charset=utf-8",
        "X-Content-Type-Options": "nosniff",
      });
      return res.end(fs.readFileSync(APP_JS));
    }
    // API 路由
    const r = matchRoute(url, req.method);
    if (!r) return sendJson(res, 404, { ok: false, error: "not found" });
    await r.handler(req, res, withParams(url, r.params));
  } catch (e) {
    sendJson(res, e.status || 500, { ok: false, error: e.message || "internal error" });
  }
});

server.listen(CONFIG.PORT, "127.0.0.1", () => {
  console.log(`clipboard running on ${CONFIG.PORT} (data: ${path.dirname(CONFIG.usersFile)})`);
});
