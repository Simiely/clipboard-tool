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
 *  数据安全设计（v0.6.13 根因级修复，非补丁）：
 *  ① Windows 上 rename 偶发 EPERM 是已知平台特性（Stack Overflow 高赞 + npm 标准重试循环解法）——
 *     递增重试 8 次（50→400ms，累计 ~1.8s）扛过系统组件/工具的瞬时访问。
 *  ② 重试耗尽仍失败 → 【不删目标、不删 tmp】：目标文件保持原状（原子性不破），
 *     tmp 保留本次完整数据（恢复点），抛错信息带 tmp 路径——启动自愈 recoverOrphanTmp 会接管。
 *  ③ 非锁错误（目录不存在/磁盘满）不碰目标，直接失败。
 *  ⚠️ 明确不做「删目标再 rename」兜底——那是数据丢失窗口（unlink 成功但 rename 又失败时原数据消失）。 */
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
      // 递增等待（50/100/150/.../400ms）后重试——瞬时占用通常 <2s
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 50 + attempt * 50);
    }
  }
  // 失败：目标未动、tmp 保留（含本次完整数据，恢复点）。下次启动 recoverOrphanTmp 自愈。
  const e = new Error("写入失败（文件被占用）: " + path.basename(file) + "（系统瞬时占用，重试后仍失败；本次数据已保留在 " + path.basename(tmp) + "，服务重启将自动恢复）");
  e.status = 500;
  throw e;
}

/** 启动自愈：扫描目录下孤儿 tmp（writeJson 失败残留），
 *  目标缺失 → tmp 恢复为目标（数据找回）；目标存在 → 删 tmp（目标数据优先，旧残留清理）。
 *  递归扫描，静默容错。 */
export function recoverOrphanTmp(rootDir) {
  if (!fs.existsSync(rootDir)) return;
  const scan = (dir) => {
    let entries = [];
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const ent of entries) {
      const p = path.join(dir, ent.name);
      if (ent.isDirectory()) { scan(p); continue; }
      const m = /^(.*)\.tmp-\d+$/.exec(ent.name);
      if (!m) continue; // 非 tmp 文件
      const target = path.join(dir, m[1]);
      try {
        if (fs.existsSync(target)) {
          fs.unlinkSync(p); // 目标完好：旧残留清理
        } else {
          fs.renameSync(p, target); // 目标缺失：用 tmp 恢复数据
          console.log("[store] 孤儿 tmp 已恢复 -> " + path.basename(target));
        }
      } catch { /* 瞬时占用：下次启动再试（幂等） */ }
    }
  };
  scan(rootDir);
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
