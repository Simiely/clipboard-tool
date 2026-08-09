// lib/routes/files.js - 文件域路由：multipart 上传 + 下载（attachment 强制下载防执行）
import fs from "node:fs";
import path from "node:path";
import { sendJson, readBody, parseMultipart, boundaryOf } from "./helpers.js";
import * as files from "../core/files.js";
import { CONFIG } from "../core/config.js";
import { requireAuth } from "./helpers.js";

export const fileRoutes = [
  { p: "/api/files", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const contentType = req.headers["content-type"] || "";
    if (!contentType.includes("multipart/form-data")) {
      return sendJson(res, 415, { ok: false, error: "需 multipart/form-data" });
    }
    const body = await readBody(req, CONFIG.MAX_FILE + 64 * 1024);
    const fields = parseMultipart(body, boundaryOf(contentType));
    const file = fields.file;
    if (!file || !file.buffer) return sendJson(res, 400, { ok: false, error: "缺少 file 字段" });
    const meta = files.saveFile(userId, {
      buffer: file.buffer,
      originalName: file.filename,
      mime: file.mime || fields.mime || "",
    });
    sendJson(res, 201, { ok: true, file: meta });
  } },
  { p: "/api/files/:fileId", m: "GET", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const fp = files.getFilePath(userId, url.params.fileId);
    const stat = fs.statSync(fp);
    // 一律 attachment：即使 SVG/HTML 也不会被浏览器执行（防 XSS）
    res.writeHead(200, {
      "Content-Type": "application/octet-stream",
      "Content-Disposition": `attachment; filename="file${path.extname(fp)}"`,
      "Content-Length": stat.size,
      "X-Content-Type-Options": "nosniff",
      "Cache-Control": "private, max-age=60",
    });
    fs.createReadStream(fp).pipe(res);
  } },
];
