# AGENTS.md · 项目规则

> 📌 **文档基线**：2026-08-15（v0.6.5：卡片系统全量重构——三区骨架+状态徽章+类型专属内容区（文本滚动/JSON代码窗/链接金钮/富文本左右对比分栏 `safeRichNodes` 白名单渲染防XSS/图片cover/文件图标卡）、统一等高 190px、26px 图标按钮、右上角只放 ✕ 删除、☆收藏/✎编辑在底部、密码弹窗方案22（下划线+浮动标签+👁显隐）；v0.6.4 见下）
> **更新文档/代码后，请更新此行**（日期 + 新 commit hash），并在 CHANGELOG 追加版本

## 技术栈
- Node.js 22.7+（ESM，`.mjs` 入口 / `lib` 下 `.js` 由 `package.json` 的 `"type": "module"` 声明），**零第三方依赖**（只用 node:http / node:fs / node:path / node:crypto / node:url）
- 平台：tools-center（manifest V2，`runtime: node` + `entry: server.mjs` + `port: 8130` + `capabilities: ["storage"]`）
- 前端：`public/index.html`（结构+CSS，**暗黑新拟态 Neumorphism**：双阴影浮雕、设计令牌全在 `:root`；v0.6.2 起为**酒红金**配色——依据 dark-design-style-guide「酒红金」#0A0A0A/#1A1A1A/#8B0000/#C9A96E/#FFFFFF，强调=酒红+金、阴影高光暖棕，改主题只动 `:root` 令牌与 users.js PALETTE，app.js 禁止硬编码颜色）+ `public/app.js`，原生 JS 零框架、无构建
- 存储：JSON 文件（原子写：tmp + rename），无数据库；WebDAV 用 HTTP 直连（Basic 认证）

## 关键坑（3~5 条，越具体越好）
- **`transform` 会劫持内部 `position:fixed` 子元素的定位（v0.6.0 实战坑）**：卡片内部挂 `position:fixed` 的图片预览浮层（`card.appendChild(box)`），若卡片 hover/active 带 `transform`，会创建 containing block 使 fixed 相对卡片而非视口 → JS 用 `getBoundingClientRect()` 算的视口坐标全部错位、浮层"飘远"。**卡片上禁用 transform 动画**，hover 层次用 box-shadow/背景表达（新拟态本就靠阴影）
- **AI 沙箱里 `fs.rmSync` 会被 WorkBuddy 注入的 shim 拦截（v0.3.1 已容错）**：WorkBuddy 通过 `NODE_OPTIONS=--require=genie-safe-delete.cjs` 猴补丁 `fs.rmSync`/`fs.rm` 为"送回收站"（`fs.rmSync.name === "wrappedRmSync"`），genie-trash 二进制在 Win11 无交互上下文返回 exit 1 + "Some operations were aborted" 但**文件实际已删除**——删除一律用 `lib/core/store.js` 的 `rmForce()`（逐个 try/catch 容错，以"文件是否存在"判定结果）。**注意：这是 AI 沙箱环境特有的行为，用户手动 `node xxx.js` 时 NODE_OPTIONS 为空、rmSync 原生正常**——文档/排查别归咎于 Node 本身
- **富文本双版本字段 `html`（v0.6.0）**：文本条目可有 `html` 字段（≤512KB，`sanitizeHtml` 净化）。语义=纯文本 `content` 用于搜索/排序/重复检测，`html` 仅作富文本复制素材。改条目模型必须同步：publicClip / createClip / updateClip / sanitizeImported 四处置 `html`，导出/WebDAV 序列化天然携带
- **平台反代注入 `__BASE__`**：前端资源/API 路径必须用 `window.__BASE__ + "/api/.."`，不能写死 `/api/..`（独立运行时 `__BASE__=""`）；前端统一走 `api(path)` / `apiBlob(path)` 封装
- **数据只写 `CAP_STORAGE_DIR`**（config.js 单点封装），不要写代码目录（可能被更新覆盖）；独立运行 fallback `./.data/`；端口从 `process.argv[2]` 读，不写死
- **排序/归拢逻辑只在服务端实现**（`lib/core/clips.js` 的 sortClips / groupByTags / groupSimilar），前端只渲染——改排序只动 clips.js，不要在前端重排
- **前端 JS 在 `public/app.js`**（v0.4.0 拆分）：改前端逻辑只动 app.js；结构/CSS 在 index.html；server.mjs 的 `/app.js` 静态路由别删
- **测试脚本已参数化（P0-2）**：`test-webdav-sync.mjs`/`test-auto-sync.mjs` 读 `TEST_PORT`（默认 8131）+ `TEST_DATA_DIR`（默认 `C:/Temp/clipboard-test`），与 smoke-test 风格一致；webdav 测试用固定用户名（"WebDAV测试"），重跑前必须清空测试数据目录或删掉残留用户，否则 409；smoke-test 已用随机后缀无此问题
- **mock-webdav 路径（P0-3）**：`dataDir` 请用绝对路径（如 `C:/Temp/mock-webdav`）；safeJoin 已统一 `path.resolve` 两侧，`/tmp/...` 相对路径在 Windows 下也正常，但建议仍用绝对路径避免歧义
- **会话已落盘**（v0.3.0）：token 存 `sessions.json`（30 天过期），重启不掉线；改密/删号/登出联动销毁——改动会话逻辑必须保持"内存缓存 + 文件持久层"（verifyToken 高频，别改成每请求读文件）
- **删除操作必须检查 API 返回值（P-101，v0.6.1）**：前端所有"删除/清空/置顶"类操作 `.catch(errToast)` 吞错后，必须 `if (r)` 守卫成功提示——曾发生 `makeDeleteBtn` 无条件 `flash("已删除")`，网络/会话失败时误报成功。新写删除类代码照此办理
- **墓碑仅在配置 WebDAV 后记录（P-102，v0.6.1）**：`deleteClip` 只返回 `{ ok, fileId, tombstone }` 不落盘；路由层 `getSyncConfig(userId)` 存在才调 `recordTombstone`——未配置同步的用户删除不再产生墓碑文件
- **拼音搜索仅前端生效（P-103）**：`renderList` 的拼音匹配只在搜索框输入路径；后端 `listClips` 的 `q` 是子串匹配（无拼音）——`?q=拼音` 直链/刷新不命中拼音，属预期，勿当 bug 修

## 约定
- UI 标签用中文；注释用中文；文件名/变量用英文
- API 返回统一 `{ ok, ... }` 或 `{ ok:false, error }`；错误带 HTTP 状态码（`store.js httpError`）
- 所有资源 id（userId/clipId/fileId）一律 UUID 白名单校验（`assertId` + `ID_RE`），防路径穿越
- 文件上传黑名单只拒可执行/脚本类（config.js BLOCKED_MIME/BLOCKED_EXT），下载**强制 attachment** + nosniff（防执行）——不要改成 inline
- WebDAV 同步语义（与 edge-multi-account-cookie 对齐）：单独删除 → 记墓碑传播删除；全部清空 → 不记墓碑（= 想从网上同步）；双向按 updatedAt 取最新
- **滚动归档**（v0.2.0）：`saveClips` 内部自动滚动超限条目进 `<uid>.archive.json`（零丢失），**同步快照只含活跃区**（归档是本地扩展）；归档条目前端只读（archived 标记）；清空/删用户/过期清扫都要覆盖归档文件
- **Windows 沙箱删除**（v0.3.1）：`rmForce` 的 unlinkSync/rmdirSync 在 safe-delete 拦截下"抛错但实际已删"——已容错吞掉；判定删除结果用"文件是否存在"，别用"是否抛错"

## 常用命令
```bash
node server.mjs 8130               # 独立运行（开发调试，数据落 ./.data/）
# 冒烟测试（独立数据目录实例；Windows 用 C:/Temp/... 绝对路径）
CAP_STORAGE_DIR=C:/Temp/clipboard-test node server.mjs 8131
TEST_PORT=8131 node scripts/smoke-test.mjs
# WebDAV 集成测试（再起 mock WebDAV；测试目录与服务器保持一致）
node scripts/mock-webdav.mjs 8180 C:/Temp/mock-webdav
TEST_PORT=8131 TEST_DATA_DIR=C:/Temp/clipboard-test node scripts/test-webdav-sync.mjs
TEST_PORT=8131 TEST_DATA_DIR=C:/Temp/clipboard-test node scripts/test-auto-sync.mjs
# 或一键（先手动起好 8131 实例 + mock-webdav 8180 后）：
npm test                          # = smoke + test:webdav + test:auto-sync
# 富文本 html 字段单测（无需服务，独立数据目录）
node scripts/test-html-field.mjs
# 复杂度测量（圈复杂度/认知复杂度/LOC）
node scripts/cc-measure.mjs public/app.js lib/core/*.js lib/routes/*.js
node --check server.mjs           # 语法检查
```

## 详细规则（按需 @引用）
- 单项目文档规范见 knowledge-base：`单项目规范/README.md`（四件套 + 文档基线断点续传）
