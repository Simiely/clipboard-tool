// lib/core/users.js - 用户域：CRUD + scrypt 密码 + 会话 token + 登录限流
// 纯业务逻辑，不碰 HTTP。路由层负责解析 token 并传入 userId。
import crypto from "node:crypto";
import path from "node:path";
import { CONFIG } from "./config.js";
import { readJson, writeJson, httpError, assertId, rmForce } from "./store.js";

// ---------- 会话（v0.3.0 起落盘持久化，重启不掉线） ----------
// 内存 Map 做读写缓存，文件 sessions.json 为持久层；token 带过期时间（默认 30 天）。
const SESSION_TTL_MS = 30 * 24 * 3600 * 1000; // 会话有效期 30 天
const sessions = new Map(); // token -> { userId, createdAt, expireAt }
let sessionsLoaded = false;
const sessionsFile = () => path.join(path.dirname(CONFIG.usersFile), "sessions.json");

/** 惰性加载会话文件到内存（首次访问时）；坏文件/不存在 → 空表 */
function ensureSessionsLoaded() {
  if (sessionsLoaded) return;
  sessionsLoaded = true;
  const data = readJson(sessionsFile(), null);
  if (data && typeof data === "object") {
    const now = Date.now();
    for (const [tk, s] of Object.entries(data)) {
      if (s && s.userId && (!s.expireAt || s.expireAt > now)) sessions.set(tk, s);
    }
  }
}

/** 会话落盘（内存 → 文件） */
function persistSessions() {
  writeJson(sessionsFile(), Object.fromEntries(sessions));
}

/** 惰性清理过期会话（返回清理数；非活跃数据不写盘，下次变化时自然收敛） */
export function pruneExpiredSessions() {
  const now = Date.now();
  let removed = 0;
  for (const [tk, s] of sessions) {
    if (s.expireAt && s.expireAt <= now) { sessions.delete(tk); removed++; }
  }
  if (removed) persistSessions();
  return removed;
}

/** 登录限流表：key(ip:userId) -> { fails, lockedUntil }。
 *  key 带目标用户——只锁「同一 IP 对同一用户」的暴力尝试，避免一人输错全用户连坐（v0.3.1 修复）。
 *  P1-3：追加惰性清理——查询时顺带回收「已过期且失败计数归零」的 key，防长运行内存缓慢增长。 */
const loginGuard = new Map();

function loginKey(ip, userId) {
  return (ip || "?") + ":" + (userId || "?");
}

/** 惰性清理限流表（内存卫生，isLoginBlocked 查询时顺带回收）：
 *  ① 清理已过期的锁定（lockedUntil 过期且失败归零）
 *  ② v0.6.11 真修复：清理「持续失败但未达阈值」的 key（fails>0 且超过 LOGIN_WINDOW_MS 未再失败）——
 *     P1-3 原实现只清①，失败 1~7 次后停手的 IP 会永久残留（假修复） */
function pruneLoginGuard() {
  const now = Date.now();
  for (const [k, g] of loginGuard) {
    if (!g) { loginGuard.delete(k); continue; }
    if (g.lockedUntil && g.lockedUntil <= now && g.fails <= 0) loginGuard.delete(k);
    else if (g.fails > 0 && now - (g.lastFailAt || 0) > CONFIG.LOGIN_WINDOW_MS) loginGuard.delete(k);
  }
}

// ---------- 用户数据 ----------

function loadUsers() {
  const list = readJson(CONFIG.usersFile, []);
  return Array.isArray(list) ? list : [];
}

function saveUsers(list) {
  writeJson(CONFIG.usersFile, list);
}

export function listUsers() {
  // 对外视图：不含 passHash；hasPass 仅暴露"是否设密"（前端据此决定是否需要密码框）
  return loadUsers().map((u) => ({ id: u.id, name: u.name, color: u.color, createdAt: u.createdAt, hasPass: !!u.passHash }));
}

function findUser(id) {
  return loadUsers().find((u) => u.id === id) || null;
}

function userFile(id) {
  return path.join(CONFIG.usersDir, id + ".json");
}

// ---------- 密码（scrypt 加盐慢哈希） ----------

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString("hex");
  const hash = crypto.scryptSync(String(password), salt, CONFIG.SCRYPT_KEYLEN).toString("hex");
  return `${salt}:${hash}`;
}

function verifyPassword(password, stored) {
  if (!stored) return true; // 未设密码
  const [salt, hash] = String(stored).split(":");
  if (!salt || !hash) return false;
  // v0.6.11：先校验 hash 为合法 hex 且长度一致——否则 timingSafeEqual 对不等长 Buffer
  // 抛 ERR_CRYPTO_TIMING_SAFE_EQUAL_LENGTH，passHash 损坏（手工编辑/半写坏文件）时登录 500 崩溃。
  if (!/^[0-9a-f]+$/i.test(hash)) return false;
  const calc = crypto.scryptSync(String(password || ""), salt, CONFIG.SCRYPT_KEYLEN).toString("hex");
  const a = Buffer.from(calc, "hex"), b = Buffer.from(hash, "hex");
  if (a.length !== b.length) return false;
  return crypto.timingSafeEqual(a, b);
}

// ---------- 会话 ----------

export function createToken(userId) {
  ensureSessionsLoaded();
  const token = crypto.randomBytes(CONFIG.TOKEN_BYTES).toString("hex");
  const now = Date.now();
  sessions.set(token, { userId, createdAt: now, expireAt: now + SESSION_TTL_MS });
  persistSessions();
  return token;
}

/** 校验 token，返回 userId；无效/过期返回 null（路由层统一在此解析，业务不碰 token） */
export function verifyToken(token) {
  if (!token) return null;
  ensureSessionsLoaded();
  const s = sessions.get(token);
  if (!s) return null;
  if (s.expireAt && s.expireAt <= Date.now()) { // 过期即删（惰性清理）
    sessions.delete(token);
    persistSessions();
    return null;
  }
  return s.userId;
}

export function destroyToken(token) {
  ensureSessionsLoaded();
  if (sessions.delete(token)) persistSessions();
}

/** 销毁某用户的全部会话（改密/删号时调用） */
function destroyUserSessions(userId, keepToken = null) {
  ensureSessionsLoaded();
  let changed = false;
  for (const [tk, s] of sessions) {
    if (s.userId === userId && tk !== keepToken) { sessions.delete(tk); changed = true; }
  }
  if (changed) persistSessions();
}

// ---------- 限流（防密码爆破；按 ip+目标用户 组合，防连坐） ----------

export function isLoginBlocked(ip, userId = "") {
  const g = loginGuard.get(loginKey(ip, userId));
  if (!g) return false;
  if (g.lockedUntil && g.lockedUntil > Date.now()) return true;
  if (g.lockedUntil) loginGuard.delete(loginKey(ip, userId)); // 仅清理"已过期的锁定"，正在累计失败的保留
  pruneLoginGuard(); // P1-3：顺带回收已过期的空 key（内存卫生，低频调用无压力）
  return false;
}

export function noteLoginFail(ip, userId = "") {
  const key = loginKey(ip, userId);
  const g = loginGuard.get(key) || { fails: 0, lockedUntil: 0, lastFailAt: 0 };
  g.fails += 1;
  g.lastFailAt = Date.now(); // v0.6.11：记录最后失败时间——未达阈值时按窗口过期清理（见 pruneLoginGuard ②）
  if (g.fails >= CONFIG.LOGIN_MAX_FAIL) {
    g.lockedUntil = Date.now() + CONFIG.LOGIN_WINDOW_MS;
    g.fails = 0;
  }
  loginGuard.set(key, g);
}

export function clearLoginFail(ip, userId = "") {
  loginGuard.delete(loginKey(ip, userId));
}

// ---------- 业务操作 ----------

/** 新建用户：昵称必填，密码可选（空 = 无密码零摩擦进入） */
export function createUser(name, password = "") {
  const n = String(name || "").trim();
  if (!n) throw httpError(400, "昵称不能为空");
  if (n.length > CONFIG.MAX_USER_NAME) throw httpError(400, `昵称最多 ${CONFIG.MAX_USER_NAME} 字`);
  if (password != null && String(password).length > CONFIG.MAX_PASSWORD) throw httpError(400, "密码过长");
  const list = loadUsers();
  if (list.some((u) => u.name === n)) throw httpError(409, "昵称已存在");
  if (list.length >= CONFIG.MAX_USERS) throw httpError(409, "用户数已达上限"); // 走查 P-6：防无限制建号占磁盘
  const user = {
    id: crypto.randomUUID(),
    name: n,
    color: PALETTE[list.length % PALETTE.length],
    passHash: password ? hashPassword(password) : "",
    createdAt: Date.now(),
  };
  list.push(user);
  saveUsers(list);
  return { id: user.id, name: user.name, color: user.color, createdAt: user.createdAt };
}

/** 登录验证：密码正确（或未设密码）→ 签发 token */
export function login(id, password = "") {
  const user = findUser(assertId(id, "用户 id"));
  if (!user) throw httpError(404, "用户不存在");
  if (!verifyPassword(password, user.passHash)) throw httpError(401, "密码错误");
  return { token: createToken(user.id), user: { id: user.id, name: user.name, color: user.color } };
}

/** 修改密码：需验证原密码（设密码用户自助改密）。
 * keepToken 为当前会话 token——改密后销毁该用户其他会话（多端安全，第二轮走查 R-4）。
 */
export function changePassword(id, oldPass, newPass, keepToken = null) {
  const uid = assertId(id, "用户 id");
  const list = loadUsers(); // 同一份数组上修改后保存，避免二次加载丢失改动
  const user = list.find((u) => u.id === uid);
  if (!user) throw httpError(404, "用户不存在");
  if (!verifyPassword(oldPass, user.passHash)) throw httpError(401, "原密码错误");
  if (newPass == null || !String(newPass).length) throw httpError(400, "新密码不能为空");
  if (String(newPass).length > CONFIG.MAX_PASSWORD) throw httpError(400, "密码过长");
  user.passHash = hashPassword(String(newPass));
  saveUsers(list);
  destroyUserSessions(uid, keepToken); // 销毁其他会话，保留当前（v0.3.0 落盘版）
  return { ok: true };
}

/** 修改用户名（v0.6.13：数据管理入口）。同名校验 409，改名不销毁会话。 */
export function renameUser(id, newName) {
  const uid = assertId(id, "用户 id");
  const n = String(newName || "").trim();
  if (!n) throw httpError(400, "昵称不能为空");
  if (n.length > CONFIG.MAX_USER_NAME) throw httpError(400, `昵称最多 ${CONFIG.MAX_USER_NAME} 字`);
  const list = loadUsers();
  const user = list.find((u) => u.id === uid);
  if (!user) throw httpError(404, "用户不存在");
  if (list.some((u) => u.id !== uid && u.name === n)) throw httpError(409, "昵称已存在");
  user.name = n;
  saveUsers(list);
  return { ok: true, name: n };
}

/** 取单用户安全视图（v0.6.13：boot 恢复登录时拉最新用户名等，防 localStorage 缓存过期/多端改名不一致） */
export function getUserPublic(id) {
  const u = findUser(assertId(id, "用户 id"));
  if (!u) throw httpError(404, "用户不存在");
  return { id: u.id, name: u.name, color: u.color, createdAt: u.createdAt, hasPass: !!u.passHash };
}

/** 取用户显示名（v0.6.13 按用户名寻址：WebDAV 快照路径用当前名，改名迁移依赖） */
export function getUserName(id) {
  const u = findUser(assertId(id, "用户 id"));
  if (!u) throw httpError(404, "用户不存在");
  return u.name;
}

/** 删除用户：同时清空其条目与文件，并销毁该用户全部会话 token
 * （否则旧 token 仍有效，可重建幽灵数据文件——走查 P-10）
 */
export function deleteUser(id) {
  const uid = assertId(id, "用户 id");
  if (!findUser(uid)) throw httpError(404, "用户不存在");
  saveUsers(loadUsers().filter((u) => u.id !== uid));
  rmForce(userFile(uid));           // 条目文件
  rmForce(userFile(uid).replace(/\.json$/, ".archive.json")); // 归档文件（滚动归档 v0.2.0）
  rmForce(path.join(CONFIG.filesDir, uid)); // 文件实体
  rmForce(path.join(CONFIG.usersDir, uid + ".tombstones.json")); // 墓碑（v0.3.1：补清理，防残留）
  rmForce(path.join(CONFIG.usersDir, uid + ".webdav.json"));    // WebDAV 配置（v0.3.1：补清理，防密码残留）
  destroyUserSessions(uid);         // 销毁该用户全部会话（v0.3.0 落盘版）
  return { ok: true };
}

// v0.6.2：用户头像色板对齐设计指南 mock——金为主 + 砖红点缀 + 暖棕系
const PALETTE = ["#C9A96E", "#D4AF37", "#AE4D4D", "#A9714B", "#B05C3B", "#8B6F47"];
