
"use strict";
const $ = (s, el=document) => el.querySelector(s);
const el = (tag, cls, txt) => { const n = document.createElement(tag); if (cls) n.className = cls; if (txt != null) n.textContent = txt; return n; };

// 子路径挂载：平台反代注入 window.__BASE__ = /tool/<id>；独立运行兜底空串
const BASE = (window.__BASE__ || "").replace(/\/+$/, "");

// ---------- 状态（单一 state 对象，页面切换只改这里） ----------
const state = { users: [], current: null, clips: [], tags: [], filter: { q: "", tag: "", type: "all", archived: false }, cols: "auto" };
const LS = { get:(k,d)=>{ try{ return JSON.parse(localStorage.getItem(k)) ?? d; }catch{ return d; } }, set:(k,v)=>localStorage.setItem(k, JSON.stringify(v)), del:(k)=>localStorage.removeItem(k) };
// 卡片复制来源抑制：点击卡片复制内容/图片时设置时间戳，clipboardchange 在窗口期内不自动弹存入窗（避免"复制即弹"）
let suppressAutoPasteUntil = 0;
// 用户选择页编辑模式状态（v0.3.1：删除用户后保持编辑状态，点击空白处退出）
let userEditMode = false;
// 待存入的富文本 html（从剪贴板 text/html 读取，存入弹窗关闭时清空；无富文本来源则为空）
let pendingHtml = "";

// ---------- API（统一带 token，10s 超时防永久挂起——第三轮 F-1） ----------
const REQ_TIMEOUT = 10000;
async function api(path, opts = {}) {
  const headers = { ...(opts.headers || {}) };
  // opts.token 显式覆盖（boot 恢复会话时 state.current 尚未设置，必须显式传 token——修复"每次刷新被踢出登录"）
  if (opts.token || state.current) headers["Authorization"] = "Bearer " + (opts.token || state.current.token);
  if (opts.json !== undefined) { headers["Content-Type"] = "application/json"; opts.body = JSON.stringify(opts.json); }
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), REQ_TIMEOUT);
  let r;
  try {
    r = await fetch(BASE + path, { ...opts, headers, signal: ctrl.signal });
  } catch (e) {
    clearTimeout(timer);
    throw new Error("请求超时或网络异常");
  }
  clearTimeout(timer);
  const data = await r.json().catch(() => ({}));
  if (r.status === 401 && state.current) handleSessionLost(); // 第二轮 R-2：会话失效自动回选用户页
  if (!r.ok) throw new Error(data.error || ("请求失败 " + r.status));
  return data;
}
async function apiBlob(path) {
  const headers = {};
  if (state.current) headers["Authorization"] = "Bearer " + state.current.token;
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), REQ_TIMEOUT);
  let r;
  try {
    r = await fetch(BASE + path, { headers, signal: ctrl.signal });
  } catch (e) {
    clearTimeout(timer);
    throw new Error("请求超时或网络异常");
  }
  clearTimeout(timer);
  if (r.status === 401 && state.current) handleSessionLost();
  if (!r.ok) throw new Error("下载失败");
  return r.blob();
}
/** 会话失效：清理本地状态回选用户页（被删号/被踢/服务重启后 token 失效） */
function handleSessionLost() {
  LS.del("cur"); state.current = null; state.clips = []; state.tags = [];
  userEditMode = false; // 回用户选择页不残留编辑模式
  resetFilter(); // v0.3.1 修复：必须用完整 resetFilter（含 type/archived），否则重进列表被 type=undefined 过滤成空白
  render();
}
/** 进入用户时重置过滤条件（第三轮 F-2：切换用户后搜索词/标签不得残留；type 保留"全部"——否则类型 Tab 无选中且列表过滤为空） */
function resetFilter() { state.filter.q = ""; state.filter.tag = ""; state.filter.type = "all"; state.filter.archived = false; }
/** 防连点锁 + 操作中视觉反馈（UI 走查 U-1：请求期间按钮禁用并置灰，用户知道正在处理） */
function guard(btn, fn) {
  return (e) => {
    if (btn._busy) return;
    btn._busy = true;
    btn.disabled = true; btn.classList.add("busy");
    const done = () => { btn._busy = false; btn.disabled = false; btn.classList.remove("busy"); };
    try {
      const r = fn(e);
      if (r && typeof r.finally === "function") r.finally(done);
      else done();
    } catch { done(); }
  };
}
/** 自定义确认弹窗（不依赖原生 confirm——预览沙箱下 confirm 行为不可靠，第二轮 R-3） */
function askConfirm(msg, onOk, okText = "确认", onCancel = null) {
  const root = $("#modal-root");
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "确认操作"));
  modal.append(el("p", "", msg));
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", okText); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = () => { m.remove(); onOk(); };
  cancel.onclick = () => { m.remove(); if (onCancel) onCancel(); };
  m.append(modal); root.append(m);
}
/** Promise 版确认：确认 resolve(true)，取消 resolve(false） */
function askConfirmP(msg, okText = "确认") {
  return new Promise((res) => askConfirm(msg, () => res(true), okText, () => res(false)));
}

// ---------- 一键复制（Clipboard API + execCommand 兜底；同步手势内完成，防 Safari 拒绝） ----------
function copyText(text) {
  if (navigator.clipboard && window.isSecureContext) {
    return navigator.clipboard.writeText(text).then(() => true, () => legacyCopy(text));
  }
  return Promise.resolve(legacyCopy(text));
}

/**
 * 富文本复制：同时写入 text/html + text/plain 双格式（剪贴板存两份）。
 * 粘贴到 Word/飞书/Notion 取 text/html 保留格式；粘贴到纯文本编辑器取 text/plain。
 * 降级：execCommand + copy 事件注入双格式（老浏览器/非安全上下文）。
 */
async function copyRich(html, text) {
  const plain = text || "";
  if (navigator.clipboard && window.isSecureContext && typeof ClipboardItem !== "undefined") {
    try {
      await navigator.clipboard.write([
        new ClipboardItem({
          "text/html": new Blob([html], { type: "text/html" }),
          "text/plain": new Blob([plain], { type: "text/plain" }),
        }),
      ]);
      return true;
    } catch { /* 落 execCommand 兜底 */ }
  }
  // 降级：隐藏容器 + Range 选中 + copy 事件注入双格式
  try {
    const holder = document.createElement("div");
    holder.innerHTML = html;
    holder.style.position = "fixed"; holder.style.top = "-9999px"; holder.style.left = "-9999px";
    document.body.appendChild(holder);
    const range = document.createRange();
    range.selectNodeContents(holder);
    const sel = window.getSelection();
    sel.removeAllRanges(); sel.addRange(range);
    const listener = (e) => {
      e.clipboardData.setData("text/html", html);
      e.clipboardData.setData("text/plain", plain);
      e.preventDefault();
    };
    document.addEventListener("copy", listener);
    const ok = document.execCommand("copy");
    document.removeEventListener("copy", listener);
    sel.removeAllRanges();
    document.body.removeChild(holder);
    return ok;
  } catch { return legacyCopy(plain); }
}

/** 纯文本 → 简单富文本 HTML（换行转段落，URL 转可点链接；仅"有富文本来源"时用，手动输入不生成） */
function textToHtml(text) {
  const esc = String(text || "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  return "<div>" + esc.split(/\n{2,}/).map(p =>
    "<p>" + p.replace(/\n/g, "<br>").replace(/(https?:\/\/\S+)/g, '<a href="$1">$1</a>') + "</p>"
  ).join("") + "</div>";
}
function legacyCopy(text) {
  try {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed"; ta.style.top = "-9999px"; ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.focus({ preventScroll: true }); ta.select(); ta.setSelectionRange(0, text.length);
    const ok = document.execCommand("copy");
    document.body.removeChild(ta);
    return ok;
  } catch { return false; }
}

function flash(msg, x, y) {
  const f = $("#flash");
  f.textContent = msg;
  if (typeof x === "number" && typeof y === "number") {
    // v0.4.2：复制成功类提示跟随鼠标点击位置显示
    f.style.left = x + "px";
    f.style.top = y + "px";
    f.classList.add("at-pos");
  } else {
    f.style.left = "";
    f.style.top = "";
    f.classList.remove("at-pos");
  }
  f.classList.add("show");
  clearTimeout(f._t);
  f._t = setTimeout(() => f.classList.remove("show"), 1400);
}
function errToast(msg) {
  const t = el("div", "toast-err", msg);
  document.body.appendChild(t);
  setTimeout(() => t.remove(), 2600);
}

function esc(s) { return String(s ?? ""); }
function fmtSize(n) { if (!n) return "0B"; if (n < 1024) return n + "B"; if (n < 1048576) return (n/1024).toFixed(1) + "KB"; return (n/1048576).toFixed(1) + "MB"; }
// v0.4.2：完整时间格式（年月日 + 时分），如 2026/08/12 13:33
function fmtTime(ts) {
  if (!ts) return "";
  const d = new Date(ts);
  const p = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
function expLabel(ts) { if (!ts) return ""; const left = ts - Date.now(); if (left <= 0) return "已过期"; if (left < 3600000) return Math.ceil(left/60000) + " 分钟后过期"; if (left < 86400000) return Math.ceil(left/3600000) + " 小时后过期"; return Math.ceil(left/86400000) + " 天后过期"; }

// ---------- 重复检测纯函数（v0.4.3：架构评估 v2 #3——比对逻辑可单测，无 DOM 副作用） ----------
/**
 * 在条目列表中查找与给定内容重复的条目（link 比 url，其他比 content）。
 * @param {string} content 待比对内容（已 trim）
 * @param {Array} clips 条目列表（含 archived 标记）
 * @returns {object|null} 重复条目（含 archived）或 null
 */
function findDuplicateClip(content, clips) {
  if (!content || !Array.isArray(clips)) return null;
  // v0.4.5：link 条目 url 兜底 ""（逻辑核验 P2-2 防御性——防 undefined 参与比对）
  return clips.find(c => (c.type === "link" ? (c.url || "") === content : (c.content || "") === content)) || null;
}

// ---------- 视图切换 ----------
function render() {
  $("#view").replaceChildren();
  if (state.current) { userEditMode = false; renderMain(); } // 进主页面必然退出编辑模式
  else renderUserSelect();
}

// ---------- 用户选择页 ----------
/** 用户选择页编辑模式开关（v0.3.1 重构）：
 *  - 进入：所有卡片右上角加 ✕ 删除按钮；退出：全部收起
 *  - 删除用户后保持编辑状态（userEditMode 全局标志，render 重建后自动恢复）
 *  - 点击页面空白处退出（见 boot 的事件委托） */
function setUserEditMode(on) {
  userEditMode = !!on;
  const grid = document.querySelector(".user-grid");
  const editAllBtn = document.querySelector(".edit-all-btn");
  if (editAllBtn) {
    editAllBtn.textContent = on ? "完成" : "编辑";
    editAllBtn.classList.toggle("on", on);
  }
  if (!grid) return;
  grid.classList.toggle("editing", on);
  grid.querySelectorAll(".user-card").forEach((card) => {
    const delBtn = card.querySelector(".del-user-btn");
    if (on && !delBtn) {
      const idx = [...grid.querySelectorAll(".user-card")].indexOf(card);
      const u = state.users[idx];
      const d = el("button", "del-user-btn", "✕");
      d.title = "删除该用户";
      d.onclick = (e) => {
        e.stopPropagation(); // 关键：在按钮层阻止冒泡，避免触发卡片 onclick 进入用户
        if (!u) return;
        askConfirm(`删除用户「${u.name}」？其全部条目与文件将被永久清除，不可恢复！`, guard(d, async () => {
          // 删除需本人身份：无密码直接登录拿 token；有密码先验证
          let token = "";
          if (u.hasPass) {
            token = await promptUserPassword(u, "验证密码以删除账号");
            if (!token) return;
          } else {
            const r = await api("/api/session", { method: "POST", json: { id: u.id, password: "" } }).catch(e2 => errToast(e2.message));
            if (!r) return;
            token = r.token;
          }
          const r = await api("/api/users/" + u.id, { method: "DELETE", token }).catch(e2 => errToast(e2.message));
          if (r) { flash("用户已删除"); await loadUsers(); render(); /* userEditMode 保持 true → 重建后仍处于编辑模式 */ }
        }), "永久删除");
      };
      card.append(d);
    } else if (!on && delBtn) {
      delBtn.remove();
    }
  });
}

async function renderUserSelect() {
  const v = $("#view");
  const hero = el("div", "hero");
  hero.style.position = "relative";
  // 全局编辑按钮：点击进入编辑模式 → 所有卡片右上角显示删除按钮（再点「完成」或点空白退出）
  const editAllBtn = el("button", "btn sm ghost edit-all-btn", "编辑");
  editAllBtn.style.cssText = "position:absolute;top:6px;right:4px";
  editAllBtn.onclick = (e) => {
    e.stopPropagation();
    setUserEditMode(!userEditMode);
  };
  hero.append(editAllBtn, el("div", "logo", "📋"), el("h1", "", "剪贴板"), el("p", "", "点击进入 · 支持新建用户 · 数据彼此隔离"));
  v.append(hero);
  const grid = el("div", "user-grid");
  if (!state.users.length) grid.append(el("div", "empty", "还没有用户，点下方新建一个"));
  for (const u of state.users) {
    const card = el("div", "user-card");
    const av = el("div", "avatar", u.name.slice(0, 1).toUpperCase());
    av.style.background = u.color;
    card.append(av, el("div", "name", u.name), el("div", "cnt", ""));
    // v0.4.5：补 guard 防连点（逻辑核验 P2-1——连点避免并发创建会话）
    card.onclick = guard(card, () => enterUser(u));
    grid.append(card);
  }
  const addBtn = el("button", "add-user", "＋ 新建用户");
  addBtn.onclick = () => openUserModal();
  grid.append(addBtn);
  v.append(grid);
  // 渲染后恢复编辑模式（删除用户后 render 重建时保持）
  if (userEditMode) setUserEditMode(true);
}
/** 密码验证弹窗（复用 openPassModal 样式）：返回 token 或 null */
function promptUserPassword(u, title) {
  return new Promise((resolve) => {
    const root = $("#modal-root");
    const m = el("div", "mask");
    const modal = el("div", "modal");
    modal.append(el("h3", "", title));
    const inp = el("input"); inp.type = "password"; inp.placeholder = `${u.name} 的密码`;
    modal.append(el("label", "", "密码"), inp);
    const row = el("div", "form-row");
    const ok = el("button", "btn primary", "验证"); ok.style.flex = "1";
    const cancel = el("button", "btn ghost", "取消");
    row.append(ok, cancel); modal.append(row);
    ok.onclick = guard(ok, async () => {
      const r = await api("/api/session", { method: "POST", json: { id: u.id, password: inp.value } }).catch(e2 => errToast(e2.message));
      if (r) { m.remove(); resolve(r.token); }
    });
    cancel.onclick = () => { m.remove(); resolve(null); };
    inp.onkeydown = e => { if (e.key === "Enter") ok.click(); };
    m.append(modal); root.append(m); inp.focus();
  });
}
async function enterUser(u) {
  if (u.hasPass) { openPassModal(u); return; }
  const r = await api("/api/session", { method: "POST", json: { id: u.id, password: "" } }).catch(e => { errToast(e.message); return null; });
  if (!r) return;
  state.current = { ...r.user, token: r.token };
  LS.set("cur", { id: r.user.id, token: r.token, name: r.user.name, color: r.user.color });
  resetFilter(); // 第三轮 F-2
  await loadClips(); render();
}
function openPassModal(u) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", `输入 ${u.name} 的密码`));
  const inp = el("input"); inp.type = "password"; inp.placeholder = "密码";
  modal.append(el("label", "", "密码"), inp);
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", "进入"); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = guard(ok, async () => {
    const r = await api("/api/session", { method: "POST", json: { id: u.id, password: inp.value } }).catch(e => { errToast(e.message); return null; });
    if (!r) return;
    state.current = { ...r.user, token: r.token };
    LS.set("cur", { id: r.user.id, token: r.token, name: r.user.name, color: r.user.color });
    resetFilter(); // 第三轮 F-2
    m.remove(); await loadClips(); render();
  });
  cancel.onclick = () => m.remove();
  inp.onkeydown = e => { if (e.key === "Enter") ok.click(); };
  m.append(modal); root.append(m); inp.focus();
}
function openUserModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "新建用户"));
  const name = el("input"); name.placeholder = "昵称";
  const pass = el("input"); pass.type = "password"; pass.placeholder = "密码（可留空）";
  modal.append(el("label", "", "昵称"), name, el("label", "", "密码（可选）"), pass);
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", "创建并进入"); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = guard(ok, async () => {
    const r = await api("/api/users", { method: "POST", json: { name: name.value, password: pass.value } }).catch(e => { errToast(e.message); return null; });
    if (!r) return;
    state.current = { ...r.user, token: r.token };
    LS.set("cur", { id: r.user.id, token: r.token, name: r.user.name, color: r.user.color });
    resetFilter(); // 第三轮 F-2
    m.remove(); await loadClips(); render();
  });
  cancel.onclick = () => m.remove();
  name.onkeydown = e => { if (e.key === "Enter") ok.click(); };
  m.append(modal); root.append(m); name.focus();
}

// ---------- 数据加载 ----------
async function loadClips() {
  const params = [];
  if (state.filter.q) params.push("q=" + encodeURIComponent(state.filter.q));
  if (state.filter.tag) params.push("tag=" + encodeURIComponent(state.filter.tag));
  if (state.filter.archived) params.push("archived=1"); // 含归档查询（滚动归档 v0.2.0）
  const [a, b] = await Promise.all([
    api("/api/clips" + (params.length ? "?" + params.join("&") : "")),
    api("/api/tags"),
  ]);
  state.clips = a.clips; state.tags = b.tags;
}
async function loadUsers() {
  const r = await api("/api/users");
  state.users = r.users;
}

// ---------- 标签选择器（交互改造：系统已有标签 chips 点选 + 输入框新建） ----------
function renderTagPicker(container, selected, allTags, onChange) {
  const render = () => {
    container.replaceChildren();
    const box = el("div", "tag-pick");
    // 展示列表 = 已有标签 ∪ 选中的新标签（新建后立即出现在选择区，保存后后端提取、刷新后仍在）
    const merged = allTags.map(t => t.tag);
    for (const s of selected) if (!merged.includes(s)) merged.push(s);
    for (const name of merged) {
      const chip = el("span", "tag" + (selected.includes(name) ? " on" : ""), name);
      chip.onclick = () => {
        const i = selected.indexOf(name);
        if (i >= 0) selected.splice(i, 1); else selected.push(name);
        onChange([...selected]);
        render(); // 立即重渲染，刷新选中高亮（修复"点击无选取反馈"）
      };
      box.append(chip);
    }
    const input = el("input"); input.placeholder = "新标签，回车添加"; input.maxLength = 20;
    input.onkeydown = (e) => {
      if (e.key === "Enter" && input.value.trim()) {
        const t = input.value.trim();
        if (!selected.includes(t)) { selected.push(t); onChange([...selected]); }
        input.value = "";
        render();
      }
    };
    box.append(input);
    container.append(box);
  };
  render();
}

// ---------- 主页面 ----------
function renderMain() {
  const v = $("#view");
  v.replaceChildren(); // 修复 U-7：renderMain 自清空——保存后直接调用时不再叠出第二套界面（用户看到 2 个输入框的根因）
  const tb = el("div", "topbar");
  tb.append(el("span", "t-logo", "📋"), el("h1", "", "剪贴板"));
  const who = el("div", "who");
  const dot = el("span", "dot"); dot.style.background = state.current.color;
  const pwBtn = el("button", "btn sm ghost", "密码");
  const dataBtn = el("button", "btn sm ghost", "数据管理");
  const syncBtn = el("button", "btn sm ghost", "一键同步"); // v0.4.2：顶栏直达 WebDAV 同步
  const logoutBtn = el("button", "btn sm ghost", "退出");
  // v0.4.2：设置拆为「密码」+「数据管理」两个入口
  pwBtn.onclick = () => openPasswordModal();
  dataBtn.onclick = () => openDataModal();
  // v0.4.2：一键同步——直接调 WebDAV 同步并刷新列表（等同数据管理弹窗的「一键同步」）
  syncBtn.onclick = guard(syncBtn, async () => {
    try {
      const r = await api("/api/sync/run", { method: "POST" });
      await loadClips(); renderTagbar(); renderList();
      flash("同步完成：远端" + (r.remoteExisted ? "有备份" : "无备份") + (r.uploaded ? " · 已上传" : " · 本地空跳过上传"));
    } catch (e) { errToast(e.message); }
  });
  // v0.4.2：删除「切换」按钮（与退出重复）——退出即销毁服务端会话并回用户选择页
  // v0.4.5：补 guard 防连点（逻辑核验 P2-1）
  logoutBtn.onclick = guard(logoutBtn, async () => {
    await api("/api/session", { method: "DELETE" }).catch(()=>{}); // 销毁服务端会话
    LS.del("cur"); state.current = null; userEditMode = false;
    await loadUsers(); render();
  });
  who.append(dot, el("span", "", state.current.name), pwBtn, dataBtn, syncBtn, logoutBtn);
  tb.append(who); v.append(tb);

  // 小入口：常态只占一行，点击或检测到复制内容时自动弹出大窗口（openPasteModal）
  const trigger = el("div", "paste-trigger", "📥 存入内容 — 点击打开，复制内容后自动弹出");
  trigger.onclick = () => openPasteModal();
  v.append(trigger);
  // 工具栏：搜索（300ms 防抖）+ 列数选择（1~4 列或自适应，记住偏好）
  const toolbar = el("div", "toolbar");
  const search = el("input"); search.type = "search"; search.placeholder = "搜索内容 / 标题 / 标签…"; search.value = state.filter.q;
  // 一边输入一边筛选：本地即时过滤（100ms 微防抖只防极速输入时的 DOM 重建，无网络请求）
  let searchTimer = null;
  search.oninput = () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      state.filter.q = search.value;
      renderList();
    }, 100);
  };
  const colsSel = el("select");
  colsSel.title = "每行显示列数";
  for (const [v, l] of [["auto", "自适应"], ["1", "1 列"], ["2", "2 列"], ["3", "3 列"], ["4", "4 列"]]) {
    const o = el("option", "", l); o.value = v; colsSel.append(o);
  }
  colsSel.value = state.cols;
  colsSel.onchange = () => {
    state.cols = colsSel.value;
    LS.set("cols", state.cols);
    renderList(); // 仅重渲染列表，不动其他区域
  };
  // 含归档开关：归档条目是只读历史（滚动归档 v0.2.0），勾选才从后端拉取合并
  const archLbl = el("label", "opt"); archLbl.style.cssText = "display:flex;align-items:center;gap:4px;color:var(--muted);font-size:12px;cursor:pointer;white-space:nowrap";
  archLbl.title = "归档只存本地，不参与 WebDAV 同步";
  const archChk = el("input"); archChk.type = "checkbox"; archChk.checked = !!state.filter.archived;
  // v0.4.5：补 guard 防连点（逻辑核验 P2-1——快速切换勾选避免并发加载竞态）
  archChk.onchange = guard(archChk, async () => {
    state.filter.archived = archChk.checked;
    await loadClips(); renderTagbar(); renderList();
  });
  archLbl.append(archChk, "含归档");
  toolbar.append(search, colsSel, archLbl); v.append(toolbar);

  // ⑥ 类型分类 Tab：全部 / 文本 / 链接 / 文件（前端过滤，数据量小无需后端）
  const TYPE_TABS = [["all", "全部"], ["text", "文本"], ["link", "链接"], ["file", "文件"]];
  const typetab = el("div", "typetab");
  const renderTypeTab = () => {
    typetab.querySelectorAll(".tt").forEach((n, i) => n.classList.toggle("on", TYPE_TABS[i][0] === state.filter.type));
  };
  for (const [v, l] of TYPE_TABS) {
    const tt = el("span", "tt" + (state.filter.type === v ? " on" : ""), l);
    tt.onclick = () => { state.filter.type = v; renderTypeTab(); renderList(); };
    typetab.append(tt);
  }
  v.append(typetab);

  renderTagbar(v);
  renderList(v);
}

function renderTagbar(v = $("#view")) {
  const old = $(".tagbar", v); if (old) old.remove();
  const bar = el("div", "tagbar");
  const all = el("span", "tag" + (state.filter.tag ? " off" : " on"), "全部");
  all.onclick = () => { state.filter.tag = ""; renderTagbar(); renderList(); }; // 前端即时过滤
  bar.append(all);
  for (const t of state.tags) {
    const c = el("span", "tag" + (state.filter.tag === t.tag ? " on" : ""), t.tag + " · " + t.count);
    c.onclick = () => { state.filter.tag = state.filter.tag === t.tag ? "" : t.tag; renderTagbar(); renderList(); };
    bar.append(c);
  }
  // v0.4.2：标签管理入口——标签行最右侧的「管理」按钮，点击弹出独立管理窗口
  const mgmtBtn = el("button", "btn sm ghost tag-mgmt-btn", "管理");
  mgmtBtn.title = "标签管理（重命名 / 删除）";
  mgmtBtn.style.marginLeft = "auto"; // 推到最右
  mgmtBtn.onclick = () => openTagManageModal();
  bar.append(mgmtBtn);
  v.append(bar);
}

// ---------- 标签管理弹窗（v0.4.2：独立窗口，由标签栏「管理」按钮打开） ----------
// 重命名 / 删除跨活跃区+归档全部条目生效；操作后刷新标签栏与列表
function openTagManageModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "标签管理"));
  const list = el("div");
  const refresh = async () => {
    list.replaceChildren();
    const tags = (await api("/api/tags").catch(() => ({ tags: [] }))).tags;
    if (!tags.length) { list.append(el("div", "empty", "还没有标签")); return; }
    for (const t of tags) {
      const row = el("div", "tag-mgmt-row");
      row.style.cssText = "display:flex;gap:6px;align-items:center;margin-bottom:6px";
      const nm = el("span", "tag", t.tag + " · " + t.count);
      nm.style.flex = "1"; nm.style.overflow = "hidden"; nm.style.textOverflow = "ellipsis";
      const rn = el("button", "btn sm ghost", "改名");
      rn.onclick = () => {
        const inp = el("input"); inp.value = t.tag; inp.maxLength = 20;
        row.replaceChildren(inp);
        inp.onkeydown = async (e) => {
          if (e.key !== "Enter" || !inp.value.trim() || inp.value.trim() === t.tag) { if (e.key === "Enter") refresh(); return; }
          const r = await api("/api/tags/" + encodeURIComponent(t.tag), { method: "PUT", json: { name: inp.value.trim() } }).catch(e2 => errToast(e2.message));
          if (r) { flash("已改名（" + r.affected + " 条）"); refresh(); refreshList(); }
        };
        inp.onblur = () => refresh();
        inp.focus();
      };
      const del = el("button", "btn sm ghost danger", "删除");
      del.onclick = () => {
        askConfirm("删除标签「" + t.tag + "」？相关条目不会删除，仅移除该标签。", guard(del, async () => {
          const r = await api("/api/tags/" + encodeURIComponent(t.tag), { method: "DELETE" }).catch(e2 => errToast(e2.message));
          if (r) { flash("已删除（" + r.affected + " 条受影响）"); refresh(); refreshList(); }
        }), "删除标签");
      };
      row.append(nm, rn, del);
      list.append(row);
    }
  };
  modal.append(list);
  const row = el("div", "form-row");
  const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
  row.append(close); modal.append(row);
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
  refresh();
}
/** 删除/编辑等低频操作后重载（与后端排序/计数保持一致；高频过滤走前端 renderList） */
async function refreshList() { await loadClips(); renderTagbar($("#view")); renderList($("#view")); }

/** 拼音首字母缩写搜索（零依赖）：GB2312 一级汉字 3755 字 → 首字母映射表（pypinyin 生成）。
 *  例："身份"→"sf"；搜索词 "sf" 可命中标题/标签含"身份"的条目。多音字取常用读音，近似即可。 */
const PY_GROUPS = {"A":"啊阿埃挨哎唉哀皑癌蔼矮艾碍爱隘鞍氨安俺按暗岸胺案肮昂盎凹敖熬翱袄傲奥懊澳","B":"芭捌扒叭吧笆八疤巴拔跋靶把耙坝霸罢爸白柏百摆佰败拜稗斑班搬扳般颁板版扮拌伴瓣半办绊邦帮梆榜膀绑棒磅蚌镑傍谤苞胞包褒剥薄雹保堡饱宝抱报暴豹鲍爆杯碑悲卑北辈背贝钡倍狈备惫焙被奔苯本笨崩绷甭泵蹦迸逼鼻比鄙笔彼碧蓖蔽毕毙毖币庇痹闭敝弊必壁臂避陛鞭边编贬扁便变卞辨辩辫遍标彪膘表鳖憋别瘪彬斌濒滨宾摈兵冰柄丙秉饼炳病并玻菠播拨钵波博勃搏铂箔伯帛舶脖膊渤驳捕卜哺补埠不布步簿部怖","C":"擦猜裁材才财睬踩采彩菜蔡餐参蚕残惭惨灿苍舱仓沧藏操糙槽曹草厕策侧册测层蹭插叉茬茶查碴搽察岔差诧拆柴豺搀掺蝉馋谗缠铲产阐颤昌猖场尝常偿肠厂敞畅唱倡超抄钞朝嘲潮巢吵炒车扯撤掣彻澈郴臣辰尘晨忱沉陈趁衬撑称城橙成呈乘程惩澄诚承逞骋秤吃痴持池迟弛驰耻齿侈尺赤翅斥炽充冲虫崇宠抽酬畴踌稠愁筹仇绸瞅丑臭初出橱厨躇锄雏滁除楚础储矗搐触处揣川穿椽传船喘串疮窗幢床闯创吹炊捶锤垂春椿醇唇淳纯蠢戳绰疵茨磁雌辞慈瓷词此刺赐次聪葱囱匆从丛凑粗醋簇促蹿篡窜摧崔催脆瘁粹淬翠村存寸磋撮搓措挫错伺畜曾椎","D":"搭达答瘩打大呆歹傣戴带殆代贷袋待逮怠耽担丹单郸掸胆旦氮但惮淡诞弹蛋当挡党荡档刀捣蹈倒岛祷导到稻悼道盗德得的蹬灯登等瞪凳邓堤低滴迪敌笛狄涤翟嫡抵底地蒂第帝弟递缔颠掂滇碘点典靛垫电佃甸店惦奠淀殿碉叼雕凋刁掉吊钓调跌爹碟蝶迭谍叠丁盯叮钉顶鼎锭定订丢东冬董懂动栋侗恫冻洞兜抖斗陡豆逗痘都督毒犊独读堵睹赌杜镀肚度渡妒端短锻段断缎堆兑队对墩吨蹲敦顿囤钝盾遁掇哆多夺垛躲朵跺舵剁惰堕","E":"蛾峨鹅俄额讹娥恶厄扼遏鄂饿恩而儿耳尔饵洱二贰","F":"发罚筏伐乏阀法珐藩帆番翻樊矾钒繁凡烦反返范贩犯饭泛坊芳方肪房防妨仿访纺放菲非啡飞肥匪诽吠肺废沸费芬酚吩氛分纷坟焚汾粉奋份忿愤粪丰封枫蜂峰锋风疯烽逢冯缝讽奉凤佛否夫敷肤孵扶拂辐幅氟符伏俘服浮涪福袱弗甫抚辅俯釜斧腑府腐赴副覆赋复傅付阜父腹负富讣附妇缚咐","G":"噶嘎该改概钙盖溉干甘杆柑竿肝赶感秆敢赣冈刚钢缸肛纲岗港杠篙皋高膏羔糕搞镐稿告哥歌搁戈鸽胳疙割革葛格阁隔铬个各给根跟耕更庚羹埂耿梗工攻功恭龚供躬公宫弓巩汞拱贡共钩勾沟苟狗垢构购够辜菇咕箍估沽孤姑鼓古蛊骨谷股故顾固雇刮瓜剐寡挂褂乖拐怪棺关官冠观管馆罐惯灌贯光广逛瑰规圭硅归龟闺轨鬼诡癸桂柜跪贵刽辊滚棍锅郭国果裹过咯傀炔","H":"蛤哈骸孩海氦亥害骇酣憨邯韩含涵寒函喊罕翰撼捍旱憾悍焊汗汉夯杭航壕嚎豪毫郝好耗号浩呵喝荷菏核禾和何合盒貉阂河涸赫褐鹤贺嘿黑痕很狠恨哼亨横衡恒轰哄烘虹鸿洪宏弘红喉侯猴吼厚候后呼乎忽瑚壶葫胡蝴狐糊湖弧虎唬护互沪户花哗华猾滑画划化话槐徊怀淮坏欢环桓还缓换患唤痪豢焕涣宦幻荒慌黄磺蝗簧皇凰惶煌晃幌恍谎灰挥辉徽恢蛔回毁悔慧卉惠晦贿秽会烩汇讳诲绘荤昏婚魂浑混豁活伙火获或惑霍货祸","J":"击圾基机畸稽积箕肌饥迹激讥鸡姬绩缉吉极棘辑籍集及急疾汲即嫉级挤几脊己蓟技冀季伎祭剂悸济寄寂计记既忌际妓继纪嘉枷夹佳家加荚颊贾甲钾假稼价架驾嫁歼监坚尖笺间煎兼肩艰奸缄茧检柬碱硷拣捡简俭剪减荐鉴践贱见键箭件健舰剑饯渐溅涧建僵姜将浆江疆蒋桨奖讲匠酱降蕉椒礁焦胶交郊浇骄娇嚼搅铰矫侥脚狡角饺缴绞剿教酵轿较叫窖揭接皆秸街阶截劫节桔杰捷睫竭洁结解姐戒藉芥界借介疥诫届巾筋斤金今津襟紧锦仅谨进靳晋禁近烬浸尽劲荆兢茎睛晶鲸京惊精粳经井警景颈静境敬镜径痉靖竟竞净炯窘揪究纠玖韭久灸九酒厩救旧臼舅咎就疚鞠拘狙疽居驹菊局咀矩举沮聚拒据巨具距踞锯俱句惧炬剧捐鹃娟倦眷卷绢撅攫抉掘倔爵觉决诀绝均菌钧军君峻俊竣浚郡骏茄","K":"槛喀咖卡开揩楷凯慨刊堪勘坎砍看康慷糠扛抗亢炕考拷烤靠坷苛柯棵磕颗科壳咳可渴克刻客课肯啃垦恳坑吭空恐孔控抠口扣寇枯哭窟苦酷库裤夸垮挎跨胯块筷侩快宽款匡筐狂框矿眶旷况亏盔岿窥葵奎魁馈愧溃坤昆捆困括扩廓阔","L":"垃拉喇蜡腊辣啦莱来赖蓝婪栏拦篮阑兰澜谰揽览懒缆烂滥琅榔狼廊郎朗浪捞劳牢老佬姥酪烙涝勒乐雷镭蕾磊累儡垒擂肋类泪棱楞冷厘梨犁黎篱狸离漓理李里鲤礼莉荔吏栗丽厉励砾历利傈例俐痢立粒沥隶力璃哩俩联莲连镰廉怜涟帘敛脸链恋炼练粮凉梁粱良两辆量晾亮谅撩聊僚疗燎寥辽潦了撂镣廖料列裂烈劣猎琳林磷霖临邻鳞淋凛赁吝拎玲菱零龄铃伶羚凌灵陵岭领另令溜琉榴硫馏留刘瘤流柳六龙聋咙笼窿隆垄拢陇楼娄搂篓漏陋芦卢颅庐炉掳卤虏鲁麓碌露路赂鹿潞禄录陆戮驴吕铝侣旅履屡缕虑氯律率滤绿峦挛孪滦卵乱掠略抡轮伦仑沦纶论萝螺罗逻锣箩骡裸落洛骆络","M":"妈麻玛码蚂马骂嘛吗埋买麦卖迈脉瞒馒蛮满蔓曼慢漫谩芒茫盲氓忙莽猫茅锚毛矛铆卯茂冒帽貌贸么玫枚梅酶霉煤没眉媒镁每美昧寐妹媚门闷们萌蒙檬盟锰猛梦孟眯醚靡糜迷谜弥米秘觅泌蜜密幂棉眠绵冕免勉娩缅面苗描瞄藐秒渺庙妙蔑灭民抿皿敏悯闽明螟鸣铭名命谬摸摹蘑模膜磨摩魔抹末莫墨默沫漠寞陌谋牟某拇牡亩姆母墓暮幕募慕木目睦牧穆","N":"拿哪呐钠那娜纳氖乃奶耐奈南男难囊挠脑恼闹淖呢馁内嫩能妮霓倪泥尼拟你匿腻逆溺蔫拈年碾撵捻念娘酿鸟尿捏聂孽啮镊镍涅您柠狞凝宁拧泞牛扭钮纽脓浓农弄奴努怒女暖虐疟挪懦糯诺辗","O":"哦欧鸥殴藕呕偶沤","P":"辟泊脯啪趴爬帕怕琶拍排牌徘湃派攀潘盘磐盼畔判叛乓庞旁耪胖抛咆刨炮袍跑泡呸胚培裴赔陪配佩沛喷盆砰抨烹澎彭蓬棚硼篷膨朋鹏捧碰坯砒霹批披劈琵毗啤脾疲皮匹痞僻屁譬篇偏片骗飘漂瓢票撇瞥拼频贫品聘乒坪苹萍平凭瓶评屏坡泼颇婆破魄迫粕剖扑铺仆莆葡菩蒲埔朴圃普浦谱曝瀑","Q":"期欺栖戚妻七凄漆柒沏其棋奇歧畦崎脐齐旗祈祁骑起岂乞企启契砌器气迄弃汽泣讫掐恰洽牵扦钎铅千迁签仟谦乾黔钱钳前潜遣浅谴堑嵌欠歉枪呛腔羌墙蔷强抢橇锹敲悄桥瞧乔侨巧鞘撬翘峭俏窍切且怯窃钦侵亲秦琴勤芹擒禽寝沁青轻氢倾卿清擎晴氰情顷请庆琼穷秋丘邱球求囚酋泅趋区蛆曲躯屈驱渠取娶龋趣去圈颧权醛泉全痊拳犬券劝缺瘸却鹊榷确雀裙群","R":"然燃冉染瓤壤攘嚷让饶扰绕惹热壬仁人忍韧任认刃妊纫扔仍日戎茸蓉荣融熔溶容绒冗揉柔肉茹蠕儒孺如辱乳汝入褥软阮蕊瑞锐闰润若弱","S":"匙撒洒萨腮鳃塞赛三叁伞散桑嗓丧搔骚扫嫂瑟色涩森僧莎砂杀刹沙纱傻啥煞筛晒珊苫杉山删煽衫闪陕擅赡膳善汕扇缮墒伤商赏晌上尚裳梢捎稍烧芍勺韶少哨邵绍奢赊蛇舌舍赦摄射慑涉社设砷申呻伸身深娠绅神沈审婶甚肾慎渗声生甥牲升绳省盛剩胜圣师失狮施湿诗尸虱十石拾时什食蚀实识史矢使屎驶始式示士世柿事拭誓逝势是嗜噬适仕侍释饰氏市恃室视试收手首守寿授售受瘦兽蔬枢梳殊抒输叔舒淑疏书赎孰熟薯暑曙署蜀黍鼠属术述树束戍竖墅庶数漱恕刷耍摔衰甩帅栓拴霜双爽谁水睡税吮瞬顺舜说硕朔烁斯撕嘶思私司丝死肆寺嗣四似饲巳松耸怂颂送宋讼诵搜艘擞嗽苏酥俗素速粟僳塑溯宿诉肃酸蒜算虽隋随绥髓碎岁穗遂隧祟孙损笋蓑梭唆缩琐索锁所厦","T":"塌他它她塔獭挞蹋踏胎苔抬台泰酞太态汰坍摊贪瘫滩坛檀痰潭谭谈坦毯袒碳探叹炭汤塘搪堂棠膛唐糖倘躺淌趟烫掏涛滔绦萄桃逃淘陶讨套特藤腾疼誊梯剔踢锑提题蹄啼体替嚏惕涕剃屉天添填田甜恬舔腆挑条迢眺跳贴铁帖厅听烃汀廷停亭庭挺艇通桐酮瞳同铜彤童桶捅筒统痛偷投头透凸秃突图徒途涂屠土吐兔湍团推颓腿蜕褪退吞屯臀拖托脱鸵陀驮驼椭妥拓唾","W":"挖哇蛙洼娃瓦袜歪外豌弯湾玩顽丸烷完碗挽晚皖惋宛婉万腕汪王亡枉网往旺望忘妄威巍微危韦违桅围唯惟为潍维苇萎委伟伪尾纬未蔚味畏胃喂魏位渭谓尉慰卫瘟温蚊文闻纹吻稳紊问嗡翁瓮挝蜗涡窝我斡卧握沃巫呜钨乌污诬屋无芜梧吾吴毋武五捂午舞伍侮坞戊雾晤物勿务悟误","X":"昔熙析西硒矽晰嘻吸锡牺稀息希悉膝夕惜熄烯溪汐犀檄袭席习媳喜铣洗系隙戏细瞎虾匣霞辖暇峡侠狭下夏吓掀锨先仙鲜纤咸贤衔舷闲涎弦嫌显险现献县腺馅羡宪陷限线相厢镶香箱襄湘乡翔祥详想响享项巷橡像向象萧硝霄削哮嚣销消宵淆晓小孝校肖啸笑效楔些歇蝎鞋协挟携邪斜胁谐写械卸蟹懈泄泻谢屑薪芯锌欣辛新忻心信衅星腥猩惺兴刑型形邢行醒幸杏性姓兄凶胸匈汹雄熊休修羞朽嗅锈秀袖绣墟戌需虚嘘须徐许蓄酗叙旭序恤絮婿绪续轩喧宣悬旋玄选癣眩绚靴薛学穴雪血勋熏循旬询寻驯巡殉汛训讯逊迅吁","Y":"压押鸦鸭呀丫芽牙蚜崖衙涯雅哑亚讶焉咽阉烟淹盐严研蜒岩延言颜阎炎沿奄掩眼衍演艳堰燕厌砚雁唁彦焰宴谚验殃央鸯秧杨扬佯疡羊洋阳氧仰痒养样漾邀腰妖瑶摇尧遥窑谣姚咬舀药要耀椰噎耶爷野冶也页掖业叶曳腋夜液一壹医揖铱依伊衣颐夷遗移仪胰疑沂宜姨彝椅蚁倚已乙矣以艺抑易邑屹亿役臆逸肄疫亦裔意毅忆义益溢诣议谊译异翼翌绎茵荫因殷音阴姻吟银淫寅饮尹引隐印英樱婴鹰应缨莹萤营荧蝇迎赢盈影颖硬映哟拥佣臃痈庸雍踊蛹咏泳涌永恿勇用幽优悠忧尤由邮铀犹油游酉有友右佑釉诱又幼迂淤于盂榆虞愚舆余俞逾鱼愉渝渔隅予娱雨与屿禹宇语羽玉域芋郁遇喻峪御愈欲狱育誉浴寓裕预豫驭鸳渊冤元垣袁原援辕园员圆猿源缘远苑愿怨院曰约越跃钥岳粤月悦阅耘云郧匀陨允运蕴酝晕韵孕轧","Z":"长匝砸杂栽哉灾宰载再在咱攒暂赞赃脏葬遭糟凿藻枣早澡蚤躁噪造皂灶燥责择则泽贼怎增憎赠扎喳渣札铡闸眨栅榨咋乍炸诈摘斋宅窄债寨瞻毡詹粘沾盏斩崭展蘸栈占战站湛绽樟章彰漳张掌涨杖丈帐账仗胀瘴障招昭找沼赵照罩兆肇召遮折哲蛰辙者锗蔗这浙珍斟真甄砧臻贞针侦枕疹诊震振镇阵蒸挣睁征狰争怔整拯正政帧症郑证芝枝支吱蜘知肢脂汁之织职直植殖执值侄址指止趾只旨纸志挚掷至致置帜峙制智秩稚质炙痔滞治窒中盅忠钟衷终种肿重仲众舟周州洲诌粥轴肘帚咒皱宙昼骤珠株蛛朱猪诸诛逐竹烛煮拄瞩嘱主著柱助蛀贮铸筑住注祝驻抓爪拽专砖转撰赚篆桩庄装妆撞壮状锥追赘坠缀谆准捉拙卓桌琢茁酌啄着灼浊兹咨资姿滋淄孜紫仔籽滓子自渍字鬃棕踪宗综总纵邹走奏揍租足卒族祖诅阻组钻纂嘴醉最罪尊遵昨左佐柞做作坐座"};
const PY_MAP = new Map();
for (const [letter, chars] of Object.entries(PY_GROUPS)) for (const ch of chars) PY_MAP.set(ch, letter);
function pyInitial(ch) { return PY_MAP.get(String(ch)) || ""; }
function strToPy(s) { let r = ""; for (const ch of String(s || "")) r += pyInitial(ch); return r; }

/** 前端即时过滤（搜索/标签/类型全本地筛——数据量小、零网络请求；删除/编辑后仍走 loadClips 保证与后端一致） */
function renderList(v = $("#view")) {
  const old = $(".list", v); if (old) old.remove();
  const list = el("div", "list");
  list.dataset.cols = state.cols; // 列数选择（1~4 或 auto）
  const kw = state.filter.q.trim().toLowerCase();
  const tg = state.filter.tag;
  const ty = state.filter.type;
  let filtered = state.clips;
  if (ty !== "all") filtered = filtered.filter(c => c.type === ty);
  if (tg) filtered = filtered.filter(c => (c.tags || []).includes(tg));
  if (kw) {
    filtered = filtered.filter(c => {
      const title = c.title || "";
      const content = c.content || "";
      const url = c.url || "";
      const tags = c.tags || [];
      if (title.toLowerCase().includes(kw) || content.toLowerCase().includes(kw) ||
          url.toLowerCase().includes(kw) || tags.some(t => t.toLowerCase().includes(kw))) return true;
      // 拼音首字母缩写匹配（标题+标签）：如 "sf" → 身份
      const py = (strToPy(title) + " " + strToPy(tags.join(" "))).toLowerCase();
      return py.includes(kw);
    });
  }
  if (!filtered.length) {
    // UI 走查 U-2：区分"没有条目"与"搜索/过滤无结果"，避免误导
    const msg = (kw || tg || ty !== "all")
      ? "没有匹配的内容 — 试试调整搜索词、标签或类型"
      : "还没有内容 — 顶部粘贴框 Ctrl+V 即存，或拖文件进来";
    list.append(el("div", "empty", msg));
  }
  for (const c of filtered) list.append(clipCard(c));
  v.append(list);
}

// ---------- 卡片工厂（v0.4.3 拆分：clipCard 从 CC49 降到组装级，各事件按钮独立小函数） ----------
/** 星标按钮（非归档卡片）；点击切换置顶并更新卡片样式 */
function makePinBtn(c, card) {
  if (c.archived) return null;
  const btn = el("button", "btn sm ghost" + (c.pinned ? " on" : ""), c.pinned ? "★" : "☆");
  btn.title = c.pinned ? "取消置顶" : "置顶";
  btn.onclick = (e) => {
    e.stopPropagation();
    guard(btn, async () => {
      const r = await api("/api/clips/" + c.id + "/pin", { method: "POST" }).catch(e2 => errToast(e2.message));
      if (!r) return;
      c.pinned = r.pinned;
      card.classList.toggle("pinned", !!r.pinned);
      btn.textContent = r.pinned ? "★" : "☆";
      btn.title = r.pinned ? "取消置顶" : "置顶";
      flash(r.pinned ? "已置顶" : "已取消置顶");
    })();
  };
  return btn;
}
/** 链接卡「打开」按钮 */
function makeOpenBtn(c) {
  const btn = el("button", "btn sm ghost", "打开");
  btn.onclick = (e) => { e.stopPropagation(); window.open(c.url, "_blank", "noopener"); };
  return btn;
}
/** 文件卡「下载」按钮 */
function makeDownloadBtn(c) {
  const btn = el("button", "btn sm ghost", "下载");
  btn.onclick = (e) => { e.stopPropagation(); downloadFile(c); };
  return btn;
}
/** 「编辑」按钮 */
function makeEditBtn(c) {
  const btn = el("button", "btn sm ghost", "编辑");
  btn.onclick = (e) => { e.stopPropagation(); openEditModal(c); };
  return btn;
}
/** 「删除」按钮（带确认） */
function makeDeleteBtn(c) {
  const btn = el("button", "btn sm ghost danger", "删除");
  btn.onclick = (e) => {
    e.stopPropagation();
    askConfirm("删除这条内容？", guard(btn, async () => {
      await api("/api/clips/" + c.id, { method: "DELETE" }).catch(e2 => errToast(e2.message));
      flash("已删除"); // UI 走查 U-5：删除成功也有反馈
      refreshList();
    }), "删除");
  };
  return btn;
}
/** JSON 格式化预览按钮（文本可解析为 JSON 时） */
function makeJsonBtn(c) {
  const btn = el("button", "btn sm ghost", "{}");
  btn.title = "JSON 格式化预览";
  btn.onclick = (e) => { e.stopPropagation(); openJsonPreview(c); };
  return btn;
}

/** 富文本复制按钮（🅡）：有 html 的文本条目显示——点它复制富文本（剪贴板写入双格式），与单击复制纯文本并存 */
function makeRichBtn(c) {
  const btn = el("button", "btn sm ghost", "🅡");
  btn.title = "复制富文本（粘贴到 Word/飞书保留格式）";
  btn.onclick = (e) => {
    e.stopPropagation();
    guard(btn, async () => {
      suppressAutoPasteUntil = Date.now() + 800; // 来源抑制：本次复制不触发自动弹窗
      const ok = await copyRich(c.html || "", c.content || "");
      if (ok) {
        flash("富文本已复制（含格式）", e.clientX, e.clientY);
        if (c.type !== "file") {
          api("/api/clips/" + c.id + "/copy", { method: "POST" }).then(() => {
            c.copyCount = (c.copyCount || 0) + 1;
            const span = $(".copycnt", e.target.closest(".clip-card"));
            if (span) span.textContent = "复制 " + c.copyCount + " 次";
          }).catch(() => {});
        }
      } else errToast("富文本复制失败");
    });
  };
  return btn;
}
/** 卡片内容预览区：图片缩略图 / 文本链接摘要 */
function makeCardPreview(c) {
  const imgUrl = () => BASE + "/api/files/" + c.fileId + "?token=" + encodeURIComponent(state.current?.token || "");
  if (c.type === "file" && (c.fileMime || "").startsWith("image/")) {
    const preview = el("div", "preview img-thumb");
    const img = el("img");
    img.loading = "lazy";
    img.alt = c.fileName || "图片";
    img.src = imgUrl();
    preview.append(img);
    return preview;
  }
  return el("div", "preview",
    c.type === "link" ? c.url : c.type === "file" ? (c.fileName + " · " + fmtSize(c.fileSize)) : c.content);
}
/** 卡片 meta 区：复制次数 / 标签（点击过滤）/ 过期 / 时间 */
function makeCardMeta(c) {
  const meta = el("div", "meta");
  meta.append(el("span", "copycnt", "复制 " + (c.copyCount || 0) + " 次"));
  for (const t of c.tags || []) {
    const tg = el("span", "badge", "#" + t);
    tg.style.cursor = "pointer";
    tg.onclick = (e) => { e.stopPropagation(); state.filter.tag = t; renderTagbar(); renderList(); };
    meta.append(tg);
  }
  if (c.expireAt) meta.append(el("span", "badge exp", expLabel(c.expireAt)));
  meta.append(el("span", "", fmtTime(c.updatedAt)));
  return meta;
}
/** 单击卡片：文本/链接复制；图片复制到剪贴板（失败降级预览）；其他文件下载 */
async function handleCardClick(c, card, e) {
  suppressAutoPasteUntil = Date.now() + 800; // 来源抑制：本次点击引起的写剪贴板不触发自动弹窗
  const px = e ? e.clientX : undefined, py = e ? e.clientY : undefined;
  if (c.type === "file") {
    if ((c.fileMime || "").startsWith("image/")) {
      try {
        const ok = await copyImageToClipboard(c);
        if (ok) flash("图片已复制，可直接粘贴", px, py);
        else { errToast("此浏览器不支持复制图片，已打开预览"); openImagePreview(c); }
      } catch { errToast("图片加载失败"); }
    } else downloadFile(c);
    return;
  }
  const text = c.type === "link" ? c.url : c.content;
  const ok = await copyText(text);
  if (ok) {
    flash("已复制", px, py);
    if (c.type !== "file") {
      api("/api/clips/" + c.id + "/copy", { method: "POST" }).then(() => {
        // 本地即时更新计数（走查 P-5：不刷新也看到 +1）
        c.copyCount = (c.copyCount || 0) + 1;
        const span = $(".copycnt", card);
        if (span) span.textContent = "复制 " + c.copyCount + " 次";
      }).catch(() => {});
    }
  } else errToast("复制失败，请手动选择复制");
}

function clipCard(c) {
  const card = el("div", "clip-card");
  if (c.pinned) card.classList.add("pinned"); // 星标卡片高亮描边
  const row1 = el("div", "row1");
  const typeBadge = el("span", "badge " + (c.type === "link" ? "link" : c.type === "file" ? "file" : "text"), c.type === "link" ? "链接" : c.type === "file" ? "文件" : "文本");
  const title = el("span", "title", c.title || (c.type === "link" ? c.url : c.type === "file" ? c.fileName : (c.content || "").slice(0, 30)));
  const ops = el("div", "ops");
  // 操作按钮（各为独立小函数，v0.4.3）
  const pin = makePinBtn(c, card); if (pin) ops.append(pin);
  if (c.type === "link") ops.append(makeOpenBtn(c));
  if (c.type === "file") ops.append(makeDownloadBtn(c));
  if (c.archived) ops.append(el("span", "badge", "归档")); // 归档只读：不提供编辑/删除（v0.2.0）
  else ops.append(makeEditBtn(c), makeDeleteBtn(c));
  if (c.type === "text" && looksLikeJson(c.content)) ops.append(makeJsonBtn(c));
  if (c.type === "text" && c.html) ops.append(makeRichBtn(c)); // 富文本条目：额外提供富文本复制按钮（🅡）
  row1.append(typeBadge, title, ops);
  card.append(row1, makeCardPreview(c), makeCardMeta(c));

  // 图片卡片 hover 预览（v0.4.3 状态显式化 + 独立函数）：默认 100%，滚轮缩放（50%~300%）
  //  - 状态收敛为单一对象 previewState（open/scale/timer/box/drag），不再散落闭包变量（防布尔失控，架构评估 v2 #1）
  //  - 浮层挂卡片内部（mouseleave 不中途消失）；wheel 绑卡片（卡片/浮层内滚动都生效）
  if (c.type === "file" && (c.fileMime || "").startsWith("image/")) bindImageHoverPreview(c, card);

  // 单击复制 / 双击编辑（v0.4.2：复制成功提示跟随鼠标点击位置）
  card.onclick = (e) => handleCardClick(c, card, e);
  card.ondblclick = (e) => { if (e.target.closest(".ops") || c.archived) return; openEditModal(c); }; // 归档只读
  return card;
}

// ---------- 图片 hover 预览绑定（v0.4.3：从 clipCard 抽出，CC 独立） ----------
function bindImageHoverPreview(c, card) {
  /** 预览状态机：open=浮层是否可见；scale=缩放比例；timer=延迟句柄；box=浮层元素；drag=拖拽进行中 */
  const previewState = { open: false, scale: 1.0, timer: null, box: null, drag: false };
  // 缩放应用：按自然尺寸×比例设宽高，框体同步调整（唯一改 scale 生效的出口）
  const applyScale = () => {
    if (!previewState.open) return;
    const img = previewState.box.querySelector("img");
    const cap = previewState.box.querySelector(".img-cap");
    img.style.width = Math.round(img.naturalWidth * previewState.scale) + "px";
    img.style.height = "auto";
    img.style.maxWidth = "none";
    if (cap) cap.textContent = c.fileName + " · " + fmtSize(c.fileSize) + " · " + Math.round(previewState.scale * 100) + "%";
    reposition();
  };
  // 缩放步进：delta>0 放大，<0 缩小；钳制 50%~300%；读配置步长（默认 15%）
  const zoom = (delta) => {
    const step = LS.get("zoomStep", 0.15) || 0.15;
    const before = previewState.scale;
    previewState.scale = Math.min(3.0, Math.max(0.5, previewState.scale + (delta > 0 ? step : -step)));
    if (previewState.scale !== before) applyScale();
  };
  // 框体尺寸 + 定位：跟随图片大小（受视口限制），显示在卡片正上方（间隙 4px，贴卡片防"飘远"）
  const reposition = () => {
    if (!previewState.open) return;
    const img = previewState.box.querySelector("img");
    const cap = previewState.box.querySelector(".img-cap");
    const pad = 20; // box padding 10*2
    const bw = Math.min(img.offsetWidth + pad, window.innerWidth - 16);
    const bh = Math.min(img.offsetHeight + (cap ? cap.offsetHeight : 20) + pad, window.innerHeight - 16);
    previewState.box.style.width = bw + "px";
    const r = card.getBoundingClientRect();
    let left = r.left + r.width / 2 - bw / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - bw - 8));
    const top = (r.top - bh - 4 >= 8) ? (r.top - bh - 4) : Math.min(r.bottom + 4, window.innerHeight - bh - 8);
    previewState.box.style.left = left + "px";
    previewState.box.style.top = top + "px";
  };
  // 打开浮层（260ms 延迟防快速划过误弹）
  const open = () => {
    clearTimeout(previewState.timer);
    previewState.timer = setTimeout(() => {
      const box = el("div", "img-hover-preview");
      const wrap = el("div", "img-hover-wrap");
      const img = el("img");
      img.src = BASE + "/api/files/" + c.fileId + "?token=" + encodeURIComponent(state.current?.token || "");
      img.onerror = () => close();
      const cap = el("div", "img-cap");
      cap.textContent = c.fileName + " · " + fmtSize(c.fileSize) + " · 100%";
      previewState.scale = 1.0; // 每次重开重置 100%
      previewState.box = box;
      previewState.open = true;
      wrap.append(img);
      box.append(wrap, cap);
      card.appendChild(box); // 挂卡片内部，防止 mouseleave 中途移除浮层
      img.onload = () => applyScale();
    }, 260);
  };
  // 关闭浮层（移除元素 + 重置状态）
  const close = () => {
    clearTimeout(previewState.timer);
    if (previewState.box) { previewState.box.remove(); previewState.box = null; }
    previewState.open = false;
    previewState.drag = false;
  };
  // 滚轮缩放（绑卡片：卡片+浮层内滚动都生效；浮层未开不拦截页面滚动）
  card.addEventListener("wheel", (e) => {
    if (!previewState.open) return;
    e.preventDefault();
    zoom(e.deltaY < 0 ? 1 : -1);
  }, { passive: false });
  // 拖拽平移：按住鼠标拖动查看放大图片的不同位置（v0.4.2）
  card.addEventListener("mousedown", (e) => {
    if (!previewState.open || e.button !== 0) return;
    const wrap = previewState.box.querySelector(".img-hover-wrap");
    if (!wrap || !wrap.contains(e.target)) return; // 仅浮层图片区可拖
    e.preventDefault();
    previewState.drag = true;
    const startX = e.clientX, startY = e.clientY;
    const sl = wrap.scrollLeft, st = wrap.scrollTop;
    const move = (ev) => { if (!previewState.drag) return; wrap.scrollLeft = sl - (ev.clientX - startX); wrap.scrollTop = st - (ev.clientY - startY); };
    const up = () => { previewState.drag = false; document.removeEventListener("mousemove", move); document.removeEventListener("mouseup", up); };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
  });
  card.addEventListener("mouseenter", open);
  card.addEventListener("mouseleave", close);
}

// ---------- 文件下载 / 图片复制到剪贴板 / 图片预览 ----------
async function downloadFile(c) {
  try {
    const blob = await apiBlob("/api/files/" + c.fileId);
    const u = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = u; a.download = c.fileName || "file";
    document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(u);
  } catch (ex) { errToast(ex.message); }
}
/** 图片转 PNG（ClipboardItem 最可靠格式；canvas 同源 blob 可用） */
function blobToPng(blob) {
  return new Promise((resolve) => {
    const img = new Image();
    const url = URL.createObjectURL(blob);
    img.onload = () => {
      try {
        const cv = document.createElement("canvas");
        cv.width = img.naturalWidth; cv.height = img.naturalHeight;
        cv.getContext("2d").drawImage(img, 0, 0);
        URL.revokeObjectURL(url);
        cv.toBlob((b) => resolve(b), "image/png");
      } catch { URL.revokeObjectURL(url); resolve(null); }
    };
    img.onerror = () => { URL.revokeObjectURL(url); resolve(null); };
    img.src = url;
  });
}
/** 复制图片到系统剪贴板（资料依据：Chrome/Edge 76+ 支持 ClipboardItem 写图片，image/png 最可靠；
 *  需安全上下文 + 点击手势；先用原格式，不支持则转 PNG，再失败返回 false 由调用方降级） */
async function copyImageToClipboard(c) {
  const tryWrite = async (b, t) => {
    if (!navigator.clipboard || !navigator.clipboard.write || typeof ClipboardItem === "undefined") return false;
    if (typeof ClipboardItem.supports === "function" && !ClipboardItem.supports(t)) return false;
    try { await navigator.clipboard.write([new ClipboardItem({ [t]: b })]); return true; } catch { return false; }
  };
  const blob = await apiBlob("/api/files/" + c.fileId);
  const type = blob.type || "image/png";
  if (await tryWrite(blob, type)) return true;
  const png = await blobToPng(blob);
  if (png && await tryWrite(png, "image/png")) return true;
  return false;
}
async function openImagePreview(c) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.style.maxWidth = "min(92vw, 760px)";
  const img = el("img");
  img.style.cssText = "max-width:100%;max-height:62vh;border-radius:8px;display:block;margin:0 auto 12px;object-fit:contain";
  modal.append(el("h3", "", c.fileName + " · " + fmtSize(c.fileSize)), img);
  const row = el("div", "form-row");
  const dl = el("button", "btn primary", "下载"); dl.style.flex = "1";
  const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
  row.append(dl, close); modal.append(row);
  dl.onclick = () => downloadFile(c);
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
  try {
    const blob = await apiBlob("/api/files/" + c.fileId);
    img.src = URL.createObjectURL(blob);
  } catch (ex) { errToast("预览失败: " + ex.message); }
}

// ---------- JSON 格式化预览（P1-7）：检测 + 美化弹窗（复制 / 覆盖保存） ----------
function looksLikeJson(s) {
  const t = String(s || "").trim();
  if (!t || (t[0] !== "{" && t[0] !== "[")) return false;
  if (t.length > 100000) return false; // 超大文本不格式化（防卡顿）
  try { JSON.parse(t); return true; } catch { return false; }
}
function openJsonPreview(c) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.style.maxWidth = "min(92vw, 760px)";
  modal.append(el("h3", "", "JSON 预览 · " + (c.title || "未命名")));
  const ta = el("textarea");
  ta.style.cssText = "min-height:320px;max-height:50vh;font-family:ui-monospace,Consolas,monospace;font-size:12px;line-height:1.5;white-space:pre;overflow:auto";
  ta.readOnly = true;
  let formatted = "";
  try { formatted = JSON.stringify(JSON.parse(c.content), null, 2); } catch { formatted = c.content; }
  ta.value = formatted;
  modal.append(ta);
  const row = el("div", "form-row");
  const copy = el("button", "btn sm", "复制美化结果"); copy.style.flex = "1";
  const save = el("button", "btn sm primary", "覆盖保存"); save.style.flex = "1";
  const close = el("button", "btn sm ghost", "关闭"); close.style.flex = "1";
  row.append(copy, save, close); modal.append(row);
  copy.onclick = guard(copy, async () => {
    const ok = await copyText(formatted);
    if (ok) flash("已复制美化 JSON"); else errToast("复制失败");
  });
  save.onclick = guard(save, async () => {
    const r = await api("/api/clips/" + c.id, { method: "PUT", json: { content: formatted } }).catch(e2 => errToast(e2.message));
    if (r) { c.content = formatted; m.remove(); flash("已覆盖保存"); refreshList(); }
  });
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
}

// ---------- 存入大弹窗（万能入口：检测到复制内容自动弹出 / 点小入口手动打开） ----------
function openPasteModal(auto = false) {
  if ($(".paste-modal")) return; // 已打开不重复弹（连续复制时用户在弹窗内自行粘贴）
  const root = $("#modal-root");
  const m = el("div", "mask");
  const modal = el("div", "modal paste-modal");
  modal.style.maxWidth = "min(92vw, 660px)";
  modal.append(el("h3", "", "存入内容"));
  const pb = el("div", "paste-box");
  const ta = el("textarea"); ta.placeholder = "粘贴文本、链接，或拖文件到这里，一键存入…";
  ta.style.minHeight = "130px";
  const typeBadge = el("div", "typebadge");
  function updateBadge() {
    const content = ta.value.trim();
    if (pickedFile) { typeBadge.textContent = "将存为：文件"; return; }
    if (!content) { typeBadge.textContent = ""; return; }
    typeBadge.textContent = "将存为：" + (/^https?:\/\/\S+$/i.test(content) ? "链接" : "文本");
  }
  ta.addEventListener("input", updateBadge);
  // Ctrl+Enter 快速存入（Enter 仍是换行）
  ta.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); save.click(); }
  });
  // 粘贴识别：图片（截图/网页图）/ 文件（资源管理器复制的文件）→ 文件流程；纯文本照常
  ta.addEventListener("paste", (e) => {
    const cd = e.clipboardData;
    if (!cd) return;
    if (cd.items) {
      for (const item of cd.items) {
        if (item.type && item.type.startsWith("image/")) {
          e.preventDefault();
          const f = item.getAsFile();
          if (f) { pick(f); flash("已接收图片，点存入即可"); return; }
        }
      }
    }
    if (cd.files && cd.files.length) {
      e.preventDefault();
      pick(cd.files[0]);
      flash(cd.files.length > 1 ? "已接收第一个文件，其余请再次粘贴" : "已接收文件，点存入即可");
    }
  });
  const chipBox = el("div");
  let pickedFile = null;
  // v0.4.1：选中图片/文件后隐藏文本输入区（存入图片不需要文字），未选文件时显示
  function syncTextareaVisibility() {
    const isFile = !!pickedFile;
    ta.classList.toggle("hidden", isFile);
    typeBadge.classList.toggle("hidden", isFile);
  }
  function pick(f) {
    if (f.size > 10 * 1024 * 1024) return errToast("文件超过 10MB 上限");
    pickedFile = f;
    chipBox.replaceChildren();
    const chip = el("div", "file-chip", "📎 " + f.name + " · " + fmtSize(f.size));
    // v0.4.1：点 ✕ 取消已选文件，恢复文本输入
    const rm = el("button", "btn sm ghost", "✕");
    rm.title = "取消选择";
    rm.onclick = (e) => { e.stopPropagation(); pickedFile = null; chipBox.replaceChildren(); syncTextareaVisibility(); updateBadge(); };
    chip.append(rm);
    chipBox.append(chip);
    syncTextareaVisibility();
    updateBadge();
  }
  const fileBtn = el("button", "btn sm", "选择文件");
  fileBtn.onclick = () => { const fi = el("input"); fi.type = "file"; fi.onchange = () => { if (fi.files[0]) pick(fi.files[0]); }; fi.click(); };
  pb.ondragover = (e) => { e.preventDefault(); pb.classList.add("drag"); };
  pb.ondragleave = () => pb.classList.remove("drag");
  pb.ondrop = (e) => { e.preventDefault(); pb.classList.remove("drag"); if (e.dataTransfer.files[0]) pick(e.dataTransfer.files[0]); };

  // 高级选项：别名 / 标签选择器 / 过期（v0.4.1：默认全部展开，不再折叠）
  const advBox = el("div", "adv-box");
  const advTitle = el("input"); advTitle.placeholder = "别名（可留空）";
  const advTagsWrap = el("div");
  const advTagsSel = [];
  const advExp = el("select");
  for (const [v, l] of [["", "永久"], ["1h", "1 小时后过期"], ["1d", "1 天后过期"], ["7d", "7 天后过期"], ["30d", "30 天后过期"]]) {
    const o = el("option", "", l); o.value = v; advExp.append(o);
  }
  renderTagPicker(advTagsWrap, advTagsSel, state.tags, (s) => { advTagsSel.length = 0; advTagsSel.push(...s); });
  advBox.append(advTitle, advTagsWrap, advExp);

  const pr = el("div", "paste-row");
  const save = el("button", "btn primary", "存入");
  const cancel = el("button", "btn ghost", "关闭");
  // v0.4.3：保存流程抽独立函数 savePasteContent（openPasteModal CC 46→拆）
  save.onclick = guard(save, async () => {
    const content = ta.value.trim();
    if (!content && !pickedFile) return errToast("先粘贴内容或选择文件");
    const adv = { title: advTitle.value, tags: [...advTagsSel], expire: advExp.value }; // v0.4.1：高级选项恒生效
    const okSave = await savePasteContent({ content, pickedFile, adv, m });
    if (okSave) { await loadClips(); renderTagbar(); renderList(); flash("已存入"); }
  });
  cancel.onclick = () => m.remove();
  pr.append(fileBtn, save, cancel);
  pb.append(ta, typeBadge, chipBox, advBox, pr);
  modal.append(pb);
  m.append(modal); root.append(m);
  ta.focus();

  // 打开时（点击手势内）自动填入剪贴板：文本优先，其次图片；文件读不到则按场景提示
  // v0.4.3：抽独立函数 autoFillPasteModal（openPasteModal CC 46→拆）
  autoFillPasteModal(ta, typeBadge, pick, auto, () => updateBadge());
}

// ---------- 存入保存流程（v0.4.3：从 openPasteModal 拆出——文件/链接/文本三分支） ----------
/**
 * 保存条目到后端；返回 true=已存入 / false=已拦截或失败。
 * 重复检测：文本/链接内容已存在 → 关存入窗，打开该条目的编辑页（标注「已有相同内容」）。
 */
async function savePasteContent({ content, pickedFile, adv, m }) {
  if (!pickedFile) {
    const dup = findDuplicateClip(content, state.clips); // v0.4.3：统一走纯函数
    if (dup) {
      m.remove(); // 关闭存入弹窗
      if (!dup.archived) openEditModal(dup, true); // 归档条目只读，仅提示不打开编辑
      else flash("已有相同内容");
      return false;
    }
  }
  try {
    if (pickedFile) {
      const fd = new FormData(); fd.append("file", pickedFile);
      const r = await api("/api/files", { method: "POST", body: fd });
      if (!r) return false;
      await api("/api/clips", { method: "POST", json: { type: "file", fileId: r.file.fileId, fileName: r.file.fileName, fileSize: r.file.fileSize, fileMime: r.file.fileMime, title: adv.title || pickedFile.name, ...adv } });
    } else if (/^https?:\/\/\S+$/i.test(content)) {
      await api("/api/clips", { method: "POST", json: { type: "link", url: content, ...adv } });
    } else {
      // 自动标题：未填别名时取首行前 20 字
      const autoTitle = content.split("\n")[0].trim().slice(0, 20);
      // 富文本：仅当剪贴板有 text/html 来源时携带（无来源则为纯文本条目，卡片只显示 1 个复制按钮）
      const html = pendingHtml || "";
      await api("/api/clips", { method: "POST", json: { type: "text", title: adv.title || autoTitle, content, ...(html ? { html } : {}), ...adv } });
    }
    pendingHtml = ""; // 存入成功清空富文本暂存
    m.remove(); // 成功关弹窗
    return true;
  } catch (e) { errToast(e.message); return false; }
}

// ---------- 自动填入剪贴板（v0.4.3：从 openPasteModal 拆出——文本优先，其次图片） ----------
/**
 * 打开存入弹窗时自动填入：剪贴板文本填入 textarea；图片仅在复制触发(auto)时拾取
 * （手动打开时若剪贴板残留图片，不自动 pick——用户可能想输文字，v0.4.2）
 */
async function autoFillPasteModal(ta, typeBadge, pick, auto, updateBadge) {
  try {
    pendingHtml = ""; // 每次打开重置，避免残留上次的富文本
    // 先读富文本：剪贴板带 text/html 时记录（有格式来源才存双版本；普通复制无此类型）
    if (navigator.clipboard && navigator.clipboard.read) {
      const items = await navigator.clipboard.read().catch(() => []);
      for (const item of items) {
        if (item.types.includes("text/html")) {
          try {
            const blob = await item.getType("text/html");
            const html = await blob.text();
            if (html && html.length < 512 * 1024) pendingHtml = html;
          } catch {}
          break;
        }
      }
    }
    if (navigator.clipboard && navigator.clipboard.readText) {
      const t = await navigator.clipboard.readText().catch(() => "");
      if (t && !ta.value) {
        ta.value = t; updateBadge(); flash("已填入剪贴板内容"); return;
      }
    }
    if (auto && navigator.clipboard && navigator.clipboard.read) {
      const items = await navigator.clipboard.read().catch(() => []);
      for (const item of items) {
        for (const type of item.types) {
          if (type.startsWith("image/")) {
            const blob = await item.getType(type);
            const f = new File([blob], "paste-image." + (type.split("/")[1] || "png"), { type });
            pick(f); flash("已接收图片，点存入即可"); return;
          }
        }
      }
    }
    if (auto) errToast("剪贴板不是文本/图片（文件请直接 Ctrl+V 粘贴或拖入输入区）");
  } catch {}
}

// ---------- 编辑弹窗 ----------
// v0.4.2：第二参数 dup=true 时标题显示「已有相同内容」常驻标记（由重复检测触发，替代一闪而过的 toast）
function openEditModal(c, dup = false) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  const h = el("h3", "", "编辑" + (c.title ? " · " + c.title : ""));
  if (dup) {
    // v0.4.2：重复提示常驻在标题下（两行），引导改标题/标签便于检索
    const dupTip = el("div", "dup-tip");
    dupTip.append(
      el("div", "dup-tip-main", "⚠️ 已有相同内容"),
      el("div", "dup-tip-sub", "可修改标题或标签，方便下次检索"),
    );
    modal.append(h, dupTip);
  } else {
    modal.append(h);
  }
  const title = el("input"); title.value = c.title || "";
  modal.append(el("label", "", "别名"), title);
  let contentInput = null, urlInput = null;
  if (c.type === "text") {
    contentInput = el("textarea"); contentInput.value = c.content; contentInput.style.minHeight = "90px";
    modal.append(el("label", "", "内容"), contentInput);
  } else if (c.type === "link") {
    urlInput = el("input"); urlInput.value = c.url;
    modal.append(el("label", "", "链接"), urlInput);
  } else {
    modal.append(el("div", "file-chip", "📎 " + c.fileName + " · " + fmtSize(c.fileSize) + "（文件不可在线改，可删除重建）"));
  }
  const editTagsSel = [...(c.tags || [])];
  const tagWrap = el("div");
  renderTagPicker(tagWrap, editTagsSel, state.tags, (s) => { editTagsSel.length = 0; editTagsSel.push(...s); });
  modal.append(el("label", "", "标签（点选已有，或输入新建）"), tagWrap);
  const expSel = el("select");
  for (const [v, l] of [["", "永久"], ["1h", "1 小时后"], ["1d", "1 天后"], ["7d", "7 天后"], ["30d", "30 天后"]]) {
    const o = el("option", "", l); o.value = v; expSel.append(o);
  }
  expSel.value = c.expireAt ? (c.expireAt - Date.now() < 7200000 ? "1h" : c.expireAt - Date.now() < 172800000 ? "1d" : c.expireAt - Date.now() < 604800000 ? "7d" : "30d") : "";
  modal.append(el("label", "", "过期时间"), expSel);
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", "保存"); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = guard(ok, async () => {
    try {
      const json = {
        title: title.value,
        tags: [...editTagsSel],
        expire: expSel.value,
      };
      if (contentInput) json.content = contentInput.value;
      if (urlInput) json.url = urlInput.value;
      await api("/api/clips/" + c.id, { method: "PUT", json }).catch(e => { errToast(e.message); return null; });
      m.remove(); flash("已保存"); refreshList();
    } catch (e) { errToast(e.message); }
  });
  cancel.onclick = () => m.remove();
  m.append(modal); root.append(m);
  title.focus(); // UI 走查 U-4：编辑弹窗聚焦标题框
  title.onkeydown = (e) => { if (e.key === "Enter" && !contentInput) ok.click(); }; // 文本 tab 内容区不拦截回车（可换行）
}

// ---------- 密码管理（v0.4.2：从设置拆出独立入口） ----------
function openPasswordModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "密码 · " + state.current.name));

  const oldPass = el("input"); oldPass.type = "password"; oldPass.placeholder = "原密码（未设密码可留空）";
  const newPass = el("input"); newPass.type = "password"; newPass.placeholder = "新密码";
  modal.append(el("label", "", "修改密码"), oldPass, newPass);
  const pwBtn = el("button", "btn sm", "保存新密码");
  pwBtn.style.width = "100%"; pwBtn.style.marginBottom = "16px";
  pwBtn.onclick = guard(pwBtn, async () => {
    try {
      await api("/api/users/" + state.current.id + "/password", {
        method: "POST", json: { oldPassword: oldPass.value, newPassword: newPass.value },
      });
      flash("密码已更新"); oldPass.value = ""; newPass.value = "";
    } catch (e) { errToast(e.message); }
  });
  modal.append(pwBtn);

  const row = el("div", "form-row");
  const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
  row.append(close); modal.append(row);
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
}

// ---------- 数据管理（v0.4.2：从设置拆出独立入口——预览/备份/清空/删号） ----------
function openDataModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "数据管理 · " + state.current.name));

  // ---------- 图片预览设置（v0.4.2：缩放步长可调） ----------
  modal.append(el("label", "", "图片预览缩放步长（% / 每格滚轮）"));
  const zoomRow = el("div", "form-row");
  const zoomStep = el("input"); zoomStep.type = "number"; zoomStep.min = "1"; zoomStep.max = "50"; zoomStep.step = "1";
  zoomStep.value = Math.round((LS.get("zoomStep", 0.15) || 0.15) * 100); // v0.4.2：默认 15%
  const zoomSave = el("button", "btn sm primary", "保存"); zoomSave.style.flex = "0 0 auto";
  zoomSave.onclick = guard(zoomSave, () => {
    let v = Math.max(1, Math.min(50, Math.round(Number(zoomStep.value) || 10)));
    zoomStep.value = v;
    LS.set("zoomStep", v / 100);
    flash("缩放步长已设为 " + v + "%");
  });
  zoomRow.append(zoomStep, zoomSave);
  modal.append(zoomRow);

  renderWebdavSection(modal); // v0.4.3：WebDAV 配置区独立函数（openDataModal 156 行拆分，架构评估 v2 #2）

  // ---------- 标签管理（P1-6）：v0.4.2 移出设置弹窗 → 标签栏「管理」按钮（openTagManageModal） ----------

  // 导出 / 导入（本地文件备份，v0.2.0——不依赖 WebDAV 的换机/归档方案）
  modal.append(el("label", "", "本地备份（导出 / 导入 JSON 文件）"));
  const bakRow = el("div", "form-row");
  const expBtn = el("button", "btn sm", "导出全部");
  const impBtn = el("button", "btn sm", "导入合并");
  const bakStatus = el("div", "dav-status");
  bakRow.append(expBtn, impBtn); modal.append(bakRow, bakStatus);
  expBtn.onclick = guard(expBtn, async () => {
    bakStatus.textContent = "导出中…";
    try {
      const r = await api("/api/export");
      const name = "clipboard-" + (state.current.name || "backup") + "-" + new Date().toISOString().slice(0, 10) + ".json";
      const blob = new Blob([JSON.stringify(r.data, null, 2)], { type: "application/json" });
      const u = URL.createObjectURL(blob);
      const a = document.createElement("a"); a.href = u; a.download = name;
      document.body.appendChild(a); a.click(); a.remove();
      URL.revokeObjectURL(u);
      bakStatus.textContent = "已导出 " + r.data.clips.length + " 条（含归档）";
      flash("导出成功");
    } catch (e) { bakStatus.textContent = "❌ " + e.message; }
  });
  impBtn.onclick = () => {
    const fi = el("input"); fi.type = "file"; fi.accept = ".json,application/json";
    fi.onchange = async () => {
      const file = fi.files[0]; if (!file) return;
      bakStatus.textContent = "导入中…";
      try {
        const text = await file.text();
        const parsed = JSON.parse(text);
        const payload = parsed && parsed.clips ? parsed : (parsed && parsed.data && parsed.data.clips ? parsed.data : null);
        if (!payload) throw new Error("不是剪贴板备份文件");
        const r = await api("/api/import", { json: { data: payload } });
        bakStatus.textContent = "导入完成：新增 " + r.added + " 条，更新 " + (r.updated || 0) + " 条，跳过 " + r.skipped + " 条，共 " + r.total + " 条";
        flash("导入完成");
        await loadClips(); renderTagbar(); renderList();
      } catch (e) { bakStatus.textContent = "❌ " + e.message; }
    };
    fi.click();
  };

  // 全部清空：清空不记墓碑（= 想从网上同步，下次 WebDAV 同步从远端恢复）
  modal.append(el("label", "", "全部清空（清空不传播删除——已做 WebDAV 备份的话，下次同步会从远端恢复）"));
  const clrBtn = el("button", "btn danger", "全部清空");
  clrBtn.style.width = "100%"; clrBtn.style.marginBottom = "16px";
  clrBtn.onclick = () => {
    askConfirm("确定全部清空？该用户所有条目将被清除；若已配置 WebDAV 备份，下次「一键同步」会从远端恢复。", guard(clrBtn, async () => {
      try {
        const r = await api("/api/clips", { method: "DELETE" });
        await loadClips(); renderTagbar(); renderList();
        flash(r.cleared ? "已全部清空（" + r.cleared + " 条）" : "已清空（本来就没有内容）");
      } catch (e) { errToast(e.message); }
    }), "全部清空");
  };
  modal.append(clrBtn);

  modal.append(el("label", "", "删除账号（不可恢复，条目与文件一并清除）"));
  const delBtn = el("button", "btn danger", "删除我的账号");
  delBtn.style.width = "100%";
  delBtn.onclick = () => {
    askConfirm("确定删除账号？该用户所有数据将被永久清除！", guard(delBtn, async () => {
      try {
        await api("/api/users/" + state.current.id, { method: "DELETE" });
        LS.del("cur"); state.current = null;
        m.remove(); await loadUsers(); render(); flash("账号已删除");
      } catch (e) { errToast(e.message); }
    }), "永久删除");
  };
  modal.append(delBtn);

  const row = el("div", "form-row");
  const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
  row.append(close); modal.append(row);
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
}

// ---------- WebDAV 配置区（v0.4.3：从 openDataModal 拆出独立函数——单一职责，架构评估 v2 #2） ----------
// 参考 edge-multi-account-cookie 设计：墓碑同步/清空不传播/双向取最新
function renderWebdavSection(modal) {
  modal.append(el("label", "", "WebDAV 备份（跨设备同步）"));
  // P1-2：归档不参与同步的显式说明（归档只存本地，同步快照只含活跃区）
  modal.append(el("div", "dav-hint", "同步范围：活跃区条目（归档只存本地，不参与 WebDAV 同步）"));
  const davUrl = el("input"); davUrl.placeholder = "服务器目录地址，如 https://dav.example.com/clipboard";
  const davUser = el("input"); davUser.placeholder = "用户名";
  const davPass = el("input"); davPass.type = "password"; davPass.placeholder = "密码（留空复用已保存）";
  modal.append(davUrl, davUser, davPass);
  // 实体同步 + 自动同步选项
  const davOpts = el("div", "dav-opts");
  const davFiles = el("input"); davFiles.type = "checkbox";
  const davFilesLbl = el("label", "opt", ""); davFilesLbl.append(davFiles, " 同步文件实体（图片/文件也备份到 WebDAV）");
  const davAuto = el("input"); davAuto.type = "checkbox";
  const davInt = el("select");
  for (const [h, l] of [[1, "1 小时"], [6, "6 小时"], [12, "12 小时"], [24, "24 小时"]]) {
    const o = el("option", "", l); o.value = h; davInt.append(o);
  }
  davInt.value = 12; // 默认 12 小时
  const davAutoLbl = el("label", "opt", ""); davAutoLbl.append(davAuto, " 自动同步 每", davInt);
  davOpts.append(davFilesLbl, davAutoLbl);
  modal.append(davOpts);
  const davRow = el("div", "form-row");
  const davTest = el("button", "btn sm", "测试保存");
  const davSync = el("button", "btn sm primary", "一键同步");
  const davStatus = el("div", "dav-status");
  davRow.append(davTest, davSync); modal.append(davRow, davStatus);
  (async () => {
    try {
      const r = await api("/api/sync/config");
      if (r.configured) {
        davUrl.value = r.url; davUser.value = r.user;
        davFiles.checked = !!r.syncFiles;
        davAuto.checked = !!r.autoSync;
        davInt.value = Math.max(1, Math.round((r.intervalMin || 720) / 60));
        davStatus.textContent = "已配置：" + r.url + (r.autoSync ? " · 每 " + davInt.value + " 小时自动同步" : "");
      }
    } catch {}
  })();
  davTest.onclick = guard(davTest, async () => {
    if (!davUrl.value.trim()) return errToast("先填服务器地址");
    davStatus.textContent = "测试中…";
    try {
      await api("/api/sync/config", { method: "POST", json: { url: davUrl.value, user: davUser.value, pass: davPass.value, syncFiles: davFiles.checked, autoSync: davAuto.checked, intervalMin: parseInt(davInt.value, 10) * 60 } });
      davStatus.textContent = "已保存：" + davUrl.value + (davAuto.checked ? " · 每 " + davInt.value + " 小时自动同步" : "") + (davFiles.checked ? " · 含文件实体" : "");
      flash("WebDAV 配置已保存");
    } catch (e) { davStatus.textContent = "❌ " + e.message; }
  });
  davSync.onclick = guard(davSync, async () => {
    davStatus.textContent = "同步中…";
    try {
      const r = await api("/api/sync/run", { method: "POST" });
      davStatus.textContent = "同步完成：远端" + (r.remoteExisted ? "有备份" : "无备份") + (r.uploaded ? " · 已上传" : " · 本地空跳过上传") + "，共 " + r.clips + " 条 / " + r.tombstones + " 墓碑";
      await loadClips(); renderTagbar(); renderList();
      flash("同步完成");
    } catch (e) { davStatus.textContent = "❌ " + e.message; }
  });
}

// ---------- 启动 ----------
// UI 走查 U-3：全局弹窗交互——点遮罩关闭 + Esc 关闭（事件委托，所有弹窗生效）
$("#modal-root").addEventListener("click", (e) => {
  if (e.target.classList && e.target.classList.contains("mask")) e.target.remove();
});
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  const masks = document.querySelectorAll("#modal-root .mask");
  const last = masks[masks.length - 1];
  if (last) last.remove();
});
async function boot() {
  state.cols = LS.get("cols", "auto") || "auto"; // 恢复列数偏好
  // 用户选择页：点击空白处退出编辑模式（v0.3.1；#view 常驻，事件委托只绑一次）
  $("#view").addEventListener("click", (e) => {
    if (!userEditMode) return;                       // 非编辑模式忽略
    if (e.target.closest(".user-card")) return;      // 点卡片本体 → 正常进入用户
    if (e.target.closest(".del-user-btn")) return;   // 点删除按钮 → 由按钮处理
    if (e.target.closest(".edit-all-btn")) return;   // 点编辑按钮 → 由按钮处理
    if (e.target.closest(".add-user")) return;       // 点新建用户 → 由按钮处理
    setUserEditMode(false);                          // 其余空白处 → 退出编辑模式
  });
  // ⑦ 剪贴板监听（2025 新 API，支持才绑定）：检测到新复制内容 → 直接弹出存入大窗口并自动填入
  if (navigator.clipboard && typeof navigator.clipboard.addEventListener === "function") {
    try {
      // v0.4.2/v0.4.3 触发逻辑（顺序：先比对，再决定弹什么；比对抽纯函数 findDuplicateClip 可单测）：
      //  1. 复制触发 → 先读剪贴板文本 → 与仓库比对（findDuplicateClip）
      //  2. 重复 → 直接弹带「已有相同内容」提示的编辑页（不弹存入窗）
      //  3. 不重复 → 正常弹存入窗并自动填入
      //  4. 卡片点击复制 → suppressAutoPasteUntil 窗口期内不弹（来源抑制）
      navigator.clipboard.addEventListener("clipboardchange", async () => {
        if (!state.current) return;    // 未登录不弹
        if (Date.now() < suppressAutoPasteUntil) return; // 来源抑制：卡片复制引起的变化不自动弹窗
        if ($(".mask")) return;        // 已有任何弹窗（编辑/设置等）时不覆盖
        let t = "";
        try {
          if (navigator.clipboard && navigator.clipboard.readText) t = (await navigator.clipboard.readText().catch(() => "")) || "";
        } catch { t = ""; }
        const content = String(t).trim();
        // 第一步：比对数据（纯函数，无副作用）
        const dup = content ? findDuplicateClip(content, state.clips) : null;
        if (dup) { // 重复 → 弹带提示的编辑页
          if (!dup.archived) openEditModal(dup, true);
          else flash("已有相同内容");
          return;
        }
        // 不重复 → 正常弹存入窗（内部自动填入剪贴板文本/图片）
        openPasteModal(true);
      });
    } catch {}
  }
  // 恢复上次登录（token 失效则回选用户页；显式传 token——否则校验请求不带鉴权永远 401）
  const saved = LS.get("cur", null);
  if (saved && saved.token) {
    try {
      const r = await api("/api/clips", { token: saved.token });
      state.current = saved;
      state.clips = r.clips;
      const t = await api("/api/tags", { token: saved.token });
      state.tags = t.tags;
    } catch { LS.del("cur"); }
  }
  if (!state.current) {
    try { await loadUsers(); } catch (e) { errToast("无法连接服务: " + e.message); } // 走查 P-9：网络异常不白屏
  }
  render();
}
boot();

