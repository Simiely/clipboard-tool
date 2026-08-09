// lib/core/users.js - 用户域：CRUD + scrypt 密码 + 会话 token + 登录限流
// 纯业务逻辑，不碰 HTTP。路由层负责解析 token 并传入 userId。
import crypto from "node:crypto";
import path from "node:path";
import { CONFIG } from "./config.js";
import { readJson, writeJson, httpError, assertId, rmForce } from "./store.js";

/** 内存会话表：token -> { userId, createdAt }（重启失效，需重新进入，可接受） */
const sessions = new Map();

/** 登录限流表：ip -> { fails, lockedUntil } */
const loginGuard = new Map();

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
  const calc = crypto.scryptSync(String(password || ""), salt, CONFIG.SCRYPT_KEYLEN).toString("hex");
  return crypto.timingSafeEqual(Buffer.from(calc, "hex"), Buffer.from(hash, "hex"));
}

// ---------- 会话 ----------

export function createToken(userId) {
  const token = crypto.randomBytes(CONFIG.TOKEN_BYTES).toString("hex");
  sessions.set(token, { userId, createdAt: Date.now() });
  return token;
}

/** 校验 token，返回 userId；无效返回 null（路由层统一在此解析，业务不碰 token） */
export function verifyToken(token) {
  const s = sessions.get(token);
  return s ? s.userId : null;
}

export function destroyToken(token) {
  sessions.delete(token);
}

// ---------- 限流（防密码爆破） ----------

export function isLoginBlocked(ip) {
  const g = loginGuard.get(ip);
  if (!g) return false;
  if (g.lockedUntil && g.lockedUntil > Date.now()) return true;
  if (g.lockedUntil) loginGuard.delete(ip); // 仅清理"已过期的锁定"，正在累计失败的保留
  return false;
}

export function noteLoginFail(ip) {
  const g = loginGuard.get(ip) || { fails: 0, lockedUntil: 0 };
  g.fails += 1;
  if (g.fails >= CONFIG.LOGIN_MAX_FAIL) {
    g.lockedUntil = Date.now() + CONFIG.LOGIN_WINDOW_MS;
    g.fails = 0;
  }
  loginGuard.set(ip, g);
}

export function clearLoginFail(ip) {
  loginGuard.delete(ip);
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
  for (const [tk, s] of sessions) {
    if (s.userId === uid && tk !== keepToken) sessions.delete(tk);
  }
  return { ok: true };
}

/** 删除用户：同时清空其条目与文件，并销毁该用户全部会话 token
 * （否则旧 token 仍有效，可重建幽灵数据文件——走查 P-10）
 */
export function deleteUser(id) {
  const uid = assertId(id, "用户 id");
  if (!findUser(uid)) throw httpError(404, "用户不存在");
  saveUsers(loadUsers().filter((u) => u.id !== uid));
  rmForce(userFile(uid));           // 条目文件
  rmForce(path.join(CONFIG.filesDir, uid)); // 文件实体
  for (const [tk, s] of sessions) if (s.userId === uid) sessions.delete(tk); // 销毁会话
  return { ok: true };
}

const PALETTE = ["#ff9292", "#7dd3fc", "#86efac", "#fcd34d", "#c4b5fd", "#f9a8d4"];
