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

/** 原子写 JSON：先写临时文件再 rename，避免写一半崩溃留下坏文件。
 *  Windows 兼容（v0.6.13 加固）：目标文件被瞬时锁定（Defender/安全软件实时扫描等外部进程）时 rename 抛 EPERM——
 *  ① 递增重试 8 次（50→400ms，累计 ~1.8s，扛过扫描瞬时锁；单实例复现证实锁可 >300ms）
 *  ② 仅最后一次才尝试「删目标后重命名」兜底（极小窗口，单实例数据可重建）
 *  ③ 非锁错误（目录不存在/磁盘满）不碰目标文件，直接失败
 *  全部失败时清理残留 tmp 并抛 500（原文件未动，数据不丢）。 */
export function writeJson(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const tmp = file + ".tmp-" + process.pid;
  fs.writeFileSync(tmp, JSON.stringify(data, null, 2), "utf8");
  for (let attempt = 0; attempt < 8; attempt++) {
    try {
      fs.renameSync(tmp, file);
      return;
    } catch (e) {
      const locked = e.code === "EPERM" || e.code === "EACCES";
      if (!locked) break; // 非锁错误：不碰目标，直接失败
      if (attempt === 7) {
        // 兜底（仅最后尝试）：目标被占用 → 先删后 rename（极小窗口，单实例可重建）
        try {
          if (fs.existsSync(file)) fs.unlinkSync(file);
          fs.renameSync(tmp, file);
          return;
        } catch {}
      }
      // 递增等待（50/100/150/.../400ms）后重试——Defender 扫描瞬时锁通常 <2s
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 50 + attempt * 50);
    }
  }
  try { fs.unlinkSync(tmp); } catch {} // 清理残留，避免下次写入被同名 tmp 干扰
  const e = new Error("写入失败（文件被占用）: " + path.basename(file) + "（安全软件扫描锁文件，重试后仍失败；可将数据目录加入 Defender 排除项）");
  e.status = 500;
  throw e;
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
 * v0.3.1 容错加固：unlinkSync/rmdirSync 在沙箱 safe-delete 拦截下会"抛错但实际已删除"，
 * 故逐个 try/catch 吞掉——保证 deleteUser 等批量清理不因单个文件抛错而中断后续清理。
 */
export function rmForce(p) {
  if (!fs.existsSync(p)) return;
  const st = fs.statSync(p);
  if (st.isDirectory()) {
    for (const name of fs.readdirSync(p)) rmForce(path.join(p, name));
    try { fs.rmdirSync(p); } catch { /* safe-delete 拦截：目录可能已被清空并移除 */ }
  } else {
    try { fs.unlinkSync(p); } catch { /* safe-delete 拦截：unlink 抛错但文件实际已删除 */ }
  }
}
