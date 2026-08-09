// lib/core/store.js - 文件系统 JSON 存储（原子写 + 路径安全）
// 平台无数据库，数据落在 CAP_STORAGE_DIR（随平台备份/WebDAV 同步）。
// 所有对外 id（userId/clipId/fileId）一律按 UUID 白名单校验，防路径穿越。
import fs from "node:fs";
import path from "node:path";
import { ID_RE } from "./config.js";

/** 读 JSON，失败/不存在返回 def（容错：坏文件不崩溃） */
export function readJson(file, def = null) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch {
    return def;
  }
}

/** 原子写 JSON：先写临时文件再 rename，避免写一半崩溃留下坏文件 */
export function writeJson(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const tmp = file + ".tmp-" + process.pid;
  fs.writeFileSync(tmp, JSON.stringify(data, null, 2), "utf8");
  fs.renameSync(tmp, file);
}

/** 校验 id 是否为合法 UUID（所有资源 id 的强制关卡） */
export function assertId(id, what = "id") {
  if (typeof id !== "string" || !ID_RE.test(id)) {
    const e = new Error(`${what} 无效`);
    e.status = 400;
    throw e;
  }
  return id;
}

/** 业务层统一错误：带 HTTP 状态码 */
export function httpError(status, message) {
  const e = new Error(message);
  e.status = status;
  return e;
}

/**
 * 确定性递归删除（绕过 Node 22 Windows 上 fs.rmSync 的 safe-delete 回收站机制，
 * 该机制在部分路径下会抛 "[safe-delete] 操作失败: Some operations were aborted"）。
 * 只用于剪贴板自身数据目录内，不碰个人目录；不存在则静默。
 */
export function rmForce(p) {
  if (!fs.existsSync(p)) return;
  const st = fs.statSync(p);
  if (st.isDirectory()) {
    for (const name of fs.readdirSync(p)) rmForce(path.join(p, name));
    fs.rmdirSync(p);
  } else {
    fs.unlinkSync(p);
  }
}
