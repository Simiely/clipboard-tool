# AGENTS.md · 项目规则

> 📌 **文档基线**：2026-08-28 · commit b55256d（v0.6.15：**批量编辑**——工具行「编辑」进入多选（卡片左上角勾选框 + 整卡点击切换选择，透明覆盖层禁用复制/双击编辑/图片 hover 预览），底部悬浮批量条（全选当前页/加标签/减标签/删除/完成）；+ **favicon**（📋 emoji data-URI）/ **宽屏自适应**（≥1280px 1440px、≥1920px 1920px）/ **空格快捷键**（主页面 Space=存入，输入框/弹窗守卫）；`getVisibleClips()` 抽纯函数供渲染与全选共用可见集；后端 `POST /api/clips/batch` 单入口按 `action` 分发 delete/addTags/removeTags——`batchDeleteClips` 跨活跃+归档删除（记墓碑 + 联动文件清理，与单条同语义）、`batchSetTags` 跨区加减标签且**必须刷新 updatedAt**（WebDAV 合并 key，不刷新则改动不同步远端/不反映排序）、空 ids/未知 action 400）。2026-08-27（v0.6.14：**测试加固——冒烟测试默认端口 8130→8131**（2026-08-27 数据清空事件教训：测试曾直连 8130 主服务污染 users.json 残留测试用户；所有测试必须指向独立数据目录实例，禁止连 8130）+ **环境清理**（杀光 20+ 遗留 server.mjs 实例，仅保留 8130/8190）+ **前端过滤单轨化**（`loadClips` 恒拉全量仅 archived 影响数据源，搜索/标签/拼音全在 `renderList` 前端过滤——修复"搜索词非空时增删改→清空搜索词全量消失"bug；后端 `listClips` q/tag 参数保留兼容）+ **墓碑规则下沉**（`webdav.recordTombstoneIfConfigured` 内部判断，路由层只编排不决策——业务规则不进 HTTP 层）+ **clips.js 拆 5 文件**（clips-store 底层底座 / clips-mutate / clips-query / clips-transfer / tombstones，单向依赖零循环，clips.js 聚合 re-export 对外路径不变）+ **webdav runSync 拆阶段**（拉源/合并/写回/实体/上传/迁移清理独立小函数，mergeSnapshots 导出不变）。v0.6.13 追加：**会话去内存缓存——sessions.json 文件即唯一真相**，直接读写文件无缓存；⚠️ 请求量达每秒几十次以上再加"内存缓存+TTL 1s"，勿提前加；loginGuard 限流表为临时状态保持内存）。2026-08-26（v0.6.13 终：**双名模型**（accountName 账号名不可变=身份键/WebDAV 寻址 + displayName 显示名可变=仅展示，改显示名零路径影响；旧数据 `accountName||name` 兼容）+ **WebDAV 按账号名寻址**（快照 `clipboard-<accountName>.json`、实体 `files/<accountName>/`，设备迁移=新部署建同名账号→同步拉回；旧格式 `clipboard-<userId>.json` 首次同步自动并入迁移；账号名修改通道 `changeAccountName` 记 `pendingNameMigrations` 数组逐个迁移，删成功才移除，连续改名不丢）+ 账号名仅限英文数字（`ACCOUNT_NAME_RE`，显示名不受限）+ 快照纳入归档（活跃∪归档完整备份，归档带 archived 标记；拉回先 saveArchive 后 saveClips）+ 归档闭环（手动归档 archiveClip/恢复 unarchiveClip updatedAt 刷新/删除 deleteArchivedClip 墓碑传播）+ 实体目录迁移（P-2：syncFileEntities pendingNames 参数，改名后旧名实体自动迁新名）+ 进程级兜底（uncaughtException/unhandledRejection 不退出）+ 同步并发锁（runSync in-flight，409「同步进行中」）+ mergeSnapshots 单元测试 + docs/main-flow.md 主线 SSOT + 富文本清除格式按钮（编辑弹窗一键转纯文本）+ 标签栏换行（全部可见可点）+ 标签筛选自愈（筛选标签无卡自动回全部）+ 修改用户名入口（renameUser=改显示名）+ 一键无饱和度配色（◐ 按钮）+ WebDAV 地址留空默认局域网 + UI 背景调深/编辑弹窗标签间距/图片预览触发区收窄/归档按钮小号化；v0.6.12：富文本复制链路修复批——S2 内联化弃用 CSSOM cssText 改字符串级解析（保 Word 私有属性 tab-interval/mso-*，word-wrap 不被改写）/ body 标签属性保留（文档级设置）+ buildWordDoc 兼容 body 片段 / body 样式双保险内联段落元素（CF_HTML Fragment 在 body 之外）/ 卡片富文本回归左右分栏 + 取消渲染预览与编辑实时预览；v0.6.11：细节审查修复批——归档去重防膨胀 / verifyPassword 损坏数据不崩溃 / 导入非 UUID id 重生成 / 富文本编辑 html 同步 / 登录限流表真修复（lastFailAt）/ WebDAV 实体扩展名兜底 .bin / readBody 超限排空 / diag.html 缓存 / 同步间隔 30min 支持；v0.6.9：富文本复制链路定稿——存入 `normalizeRichHtml` 内联化 + 复制 `buildWordDoc` 包装 xmlns:o/w/m + `execCommandRich` 主路径，Word 粘贴保留完整格式；v0.6.8 富文本链路重构 + 诊断页 diag.html；v0.6.7 WebDAV 远端子目录 workbuddy/剪贴板/；v0.6.6 弹窗四件套重设计（密码/数据管理/存入/编辑）+ 重复检测单弹窗 + 窄屏适配；v0.6.5 卡片系统全量重构见下）
> **更新文档/代码后，请更新此行**（日期 + 新 commit hash），并在 CHANGELOG 追加版本

## 技术栈
- Node.js 22.7+（ESM，`.mjs` 入口 / `lib` 下 `.js` 由 `package.json` 的 `"type": "module"` 声明），**零第三方依赖**（只用 node:http / node:fs / node:path / node:crypto / node:url）
- 平台：tools-center（manifest V2，`runtime: node` + `entry: server.mjs` + `port: 8130` + `capabilities: ["storage"]`）
- 前端：`public/index.html`（结构+CSS，**暗黑新拟态 Neumorphism**：双阴影浮雕、设计令牌全在 `:root`；v0.6.2 起为**酒红金**配色——依据 dark-design-style-guide「酒红金」#0A0A0A/#1A1A1A/#8B0000/#C9A96E/#FFFFFF，强调=酒红+金、阴影高光暖棕，改主题只动 `:root` 令牌与 users.js PALETTE，app.js 禁止硬编码颜色）+ `public/app.js`，原生 JS 零框架、无构建
- 存储：JSON 文件（原子写：tmp + rename），无数据库；WebDAV 用 HTTP 直连（Basic 认证）
- **EXE 桌面版**（`clipboard-exe/`，v0.7.0 MVP）：C# WinForms **net9.0-windows**（官方深色 `Application.SetColorMode(SystemColorMode.Dark)`，.NET 8 无此 API）；剪贴板监听 `AddClipboardFormatListener`；数据 `ClipItem` 16 字段与 Web `publicClip` 对齐（Web ↔ EXE 导出 JSON 互导）；单文件发布约 200 KB（框架依赖，用户装 .NET 9 Desktop Runtime）；数据存 exe 同目录 `data/`（便携迁移）

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
- **归档滚动必须按 id 去重（v0.6.11）**：`rollToArchive` 追加归档前按 id 去重——否则每次 saveClips（含 WebDAV 同步写回）都把同一批最旧条目重复滚入，归档指数膨胀。新增滚动测试用例要同时验证「不重复」与「新条目可滚入」
- **导入 id 必须校验 UUID（v0.6.11）**：`sanitizeImported` 的 id 走 `ID_RE` 校验，非 UUID 重新生成——否则导入的条目后续 assertId 全拒，编辑/复制/删除全 400。**已存在**的导出数据 id 都是 UUID，此修复针对外部工具备份/手工 JSON
- **富文本条目编辑后 html 必须同步（v0.6.11）**：前端编辑/JSON 覆盖保存若改 content 且条目有 html，必须同步重建 html（textToHtml）——否则右栏预览与复制拿到的还是旧内容。只改 html 不动 content 不会出现（后端 updateClip 两者独立可改）
- **Word 私有 CSS 属性必须字符串级处理（v0.6.12）**：`normalizeRichHtml` 收集 `<style>` 块规则**绝不能过 CSSOM `rule.style.cssText`**——它只序列化浏览器认识的属性，`tab-interval` / `text-justify-trim` / `mso-*` 全被丢弃、`word-wrap` 被规范化为 `overflow-wrap`（Word/WPS 粘贴还原靠这些）。必须用 `st.textContent` 正则（`/([^{}]+)\{([^{}]*)\}/g`，跳过 `@` 规则）字符串级解析原始声明。另外：**body 标签自身属性不在 `doc.body.innerHTML` 里**——Word 文档级设置（tab-interval 等）写在 `<body>` 上，必须遍历 `body.attributes` 保留 `<body attrs>`，并按 CF_HTML 规范（粘贴应用主要解析 Fragment，body 在 Fragment 外）把 body style 内联到段落元素做双保险
- **verifyPassword 先校验 hash 格式（v0.6.11）**：`timingSafeEqual` 对不等长 Buffer 抛 ERR_CRYPTO_TIMING_SAFE_EQUAL_LENGTH——passHash 损坏时登录会 500。先 `^[0-9a-f]+$` + 长度一致再比较，损坏数据安全返回 401
- **登录限流表需 lastFailAt（v0.6.11）**：`pruneLoginGuard` 除清理锁定过期 key 外，还要清理「fails>0 且超过 LOGIN_WINDOW_MS 未再失败」的 key——否则失败 1~7 次后停手的 IP 永久残留（P1-3 曾假修复）

## 约定
- **主线总览（SSOT）见 `docs/main-flow.md`**——"一条剪贴板的生命周期"一图流 + 各步入口函数；改主线流程/入口后必须同步该文档（行号会漂移，以函数名为准）
- UI 标签用中文；注释用中文；文件名/变量用英文
- API 返回统一 `{ ok, ... }` 或 `{ ok:false, error }`；错误带 HTTP 状态码（`store.js httpError`）
- 所有资源 id（userId/clipId/fileId）一律 UUID 白名单校验（`assertId` + `ID_RE`），防路径穿越
- 文件上传黑名单只拒可执行/脚本类（config.js BLOCKED_MIME/BLOCKED_EXT），下载**强制 attachment** + nosniff（防执行）——不要改成 inline
- WebDAV 同步语义（与 edge-multi-account-cookie 对齐）：单独删除 → 记墓碑传播删除；全部清空 → 不记墓碑（= 想从网上同步）；双向按 updatedAt 取最新
- **双名模型（v0.6.13）**：`accountName` 账号名不可变（身份键：WebDAV 快照/实体寻址、跨设备识别）与 `displayName` 显示名可变（仅展示）分离——**改显示名绝不影响任何路径/寻址**；设备迁移 = 新部署建相同账号名 → 同步拉回；新建用户必填账号名、显示名可留空（默认=账号名）；旧数据兼容（`u.accountName || u.name` / `u.displayName || u.name`，勿改读取归一逻辑）
- **滚动归档**（v0.2.0）：`saveClips` 内部自动滚动超限条目进 `<uid>.archive.json`（零丢失）；**v0.6.13 起同步快照 = 活跃区 ∪ 归档区**（归档条目带 `archived` 标记，WebDAV 完整备份）；拉回分拣写回**顺序关键：先 `saveArchive`（归档组替换）再 `saveClips`（活跃组，内部滚动会追加进归档）**——反了会覆盖刚滚出的条目；归档条目前端只读（archived 标记）+ ✕ 可删除（deleteArchivedClip，墓碑传播远端）；清空/删用户/过期清扫都要覆盖归档文件
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
