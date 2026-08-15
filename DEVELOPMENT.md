# DEVELOPMENT.md · 开发文档

## 项目概览

clipboard-tool 是 tools-center 平台的一个**多用户剪贴板管理工具**：粘贴自动识别（URL→链接 / 其他→文本 / 图片文件→文件条目）、一键复制、双击编辑、标签体系、拼音首字母搜索、智能排序（复制次数→标签相近→内容相似）、可选密码锁、WebDAV 备份同步。Node 22+ 零依赖，JSON 文件存储，双模式运行（平台托管 / 独立运行）。

## 架构说明

```
server.mjs (入口薄层:静态服务 + 路由分发 + 过期清扫 60s + 自动同步 60s)
  ├── lib/core/        纯业务逻辑（不碰 HTTP；.js 扩展名，由 package.json type:module 声明 ESM）
  │   ├── config.js    全局配置单一来源（端口/输入边界/上传黑名单/UUID 白名单）
  │   ├── store.js     JSON 原子读写 + assertId + httpError + rmForce
  │   ├── clips.js     条目域：CRUD + 复制计数 + 排序/搜索/标签 + 墓碑 + 过期清扫
  │   ├── users.js     用户域：scrypt 密码 + 会话 token（内存缓存+文件落盘）+ 登录限流
  │   ├── files.js     文件域：上传边界 + 归属校验 + 物理存取
  │   └── webdav.js    WebDAV 备份同步：双向合并 + 墓碑裁决 + 实体同步 + 定时自动同步
  ├── lib/routes/      路由薄层（分段匹配零正则 + 会话中间件）
  │   ├── index.js     路由注册表 + matchRoute/withParams
  │   ├── helpers.js   sendJson / jsonBody / multipart 解析 / requireAuth
  │   └── clips.js · users.js · files.js · sync.js
  └── public/index.html  单文件前端（用户选择页 + 主页面 + 弹窗体系）
```

**数据流**：前端 `fetch(BASE + "/api/..")`（Bearer token）→ server.mjs 路由匹配 → lib/routes 薄层（鉴权/读 body）→ lib/core 业务层（JSON 文件读写）→ 返回 `{ ok, ... }`。

**数据布局**（全部在 `CAP_STORAGE_DIR` 内）：
- `users.json`：用户列表（含 passHash）
- `sessions.json`：会话表（v0.3.0 落盘，token → { userId, createdAt, expireAt }，30 天过期）
- `users/<uid>.json`：每人活跃条目数组（上限 500 条，超出滚动进归档）
- `users/<uid>.archive.json`：每人归档（v0.2.0 滚动归档，最旧条目按 createdAt 移入，只读展示）
- `users/<uid>.tombstones.json`：每人墓碑（删除传播记录，90 天 TTL）
- `users/<uid>.webdav.json`：每人 WebDAV 配置（含明文密码）
- `files/<uid>/<fileId>.<ext>`：文件实体（随机名防路径穿越）

**排序（服务端单一实现）**：① pinned 置顶 → ② copyCount 降序 → ③ 标签相近归拢（Map 倒排）→ ④ 内容相似归拢（10 字符 ngram 倒排索引，O(n×L) 非 O(n²)）。读取时计算，任何修改即时反映。

## 关键问题与方案

### 问题：AI 沙箱里 `fs.rmSync` 报 `[safe-delete] 操作失败`（v0.3.1 容错 / 2026-08-14 澄清根源）

**TL;DR**：WorkBuddy（AI 沙箱）通过 `NODE_OPTIONS=--require=genie-safe-delete.cjs` 注入安全删除 shim，把 `fs.rmSync`/`fs.rm` 猴补丁为"送回收站"（genie-trash 二进制），该二进制在 Windows 11 无交互上下文返回 exit 1 + "Some operations were aborted"，但文件实际已删除。删除一律用自写 `rmForce()`（逐个 try/catch 容错）。

- 现象：删除用户数据/文件实体时 `fs.rmSync(path, { recursive: true, force: true })` 抛 `[safe-delete] 操作失败: Some operations were aborted`
- 根因（2026-08-14 实测澄清，非 Node 22 原生行为）：`fs.rmSync.name === "wrappedRmSync"`——被 WorkBuddy 注入的 genie-safe-delete.cjs shim 包裹；shim 把"回收站操作被系统中止"（E_ABORT/DE_OPCANCELLED，无交互桌面下 IFileOperation 常见）误判为删除失败并抛错，但删除实际完成
- 边界：**仅 AI 沙箱环境触发**（用户手动 `node xxx.js` 时 NODE_OPTIONS 为空，rmSync 原生正常）；且非全局——用户主目录/项目目录抛错，`C:\Temp` 等部分路径正常（与回收站/受保护目录相关）
- 解决：`lib/core/store.js` 实现 `rmForce()`——stat 判断类型后手动 `readdir + 递归` 或 `unlink`，每步 try/catch 吞掉（"抛错但实际已删"），以"文件是否存在"判定结果
- 预防：项目内所有删除路径（删用户/删文件/过期清扫）统一走 `rmForce`；排查删除类报错先看 NODE_OPTIONS 是否有 genie-safe-delete，别归咎于 Node 本身

### 问题：平台反代挂在 `/tool/<id>/` 子路径，前端路径写死会 404

**TL;DR**：前端一律 `window.__BASE__ + "/api/.."`；独立运行时平台不注入 `__BASE__`，兜底 `window.__BASE__ || ""`。

- 问题：工具在平台里被反代到 `/tool/clipboard/`，写死 `/api/clips` 会打到平台自身 API
- 根因：tools-center proxy.js 只向 HTML 注入 `<script>window.__BASE__="/tool/<id>";</script>`
- 解决：页面加载先 `const BASE = (window.__BASE__ || "").replace(/\/+$/, "")`；所有请求走 `api(path)` / `apiBlob(path)` 封装
- 预防：新增前端请求时统一走 api 封装，不要散用 fetch

### 问题：删除与"全部清空"的同步语义必须相反（WebDAV）

**TL;DR**：单独删除 → 记墓碑（deletedAt）→ 下次同步传播删除（防旧备份把已删条目复活）；全部清空 → 不记墓碑（= 用户"想从网上同步"，下次同步从远端拉回恢复）。

- 问题：若删除也传播、清空也传播，用户清空后想从备份恢复却把远端也清空
- 根因：两种操作的意图完全不同——删除是"这个条目不要了"，清空是"本地重来"
- 解决：`deleteClip` 记墓碑；`clearAllClips` 清空墓碑。合并裁决：墓碑 `deletedAt > 条目 updatedAt` → 删除；条目 `updatedAt > 墓碑 deletedAt` → 保留（删后又被编辑）
- 预防：改同步语义前先想清楚"用户意图"，墓碑是防复活的关键，90 天 TTL 防无限增长

### 问题：清空后上传会把远端备份覆盖成空（数据丢失风险）

**TL;DR**：合并前本地无数据（新设备 / 清空后）→ 跳过上传，只拉回远端——防空备份覆盖远端。

- 问题：清空本地后一键同步，远端快照被空数组覆盖，另一台设备恢复时数据全丢
- 根因：上传逻辑没区分"本地没数据"与"本地想清空"
- 解决：`runSync` 中 `hadLocal = localClips.length > 0 || localTomb.length > 0`；为 false 时跳过 `uploadSnapshot`，远端数据已拉回本地，下次同步本地非空自然收敛
- 预防：任何"双向合并"都必须有"空侧不覆盖"保护

### 问题：内容相似归拢用两两全文比较会 O(n²·L³) 爆炸

**TL;DR**：用 10 字符 ngram 倒排索引判定相似——建索引 O(n×L)、查询 O(L×桶大小)，100 条毫秒级。

- 问题：按"内容共享 ≥10 字符片段"归拢，朴素两两比较在条目多时指数级变慢
- 根因：`for i for j` 全比较 + 每次 slice 比较
- 解决：`groupSimilar` 把每条内容切成 10-gram 建 `Map<gram, Set<下标>>`，查询时用条目的 grams 反查同桶下标，标记 used 防止重复归拢
- 预防：排序类需求优先考虑倒排索引而非两两比较；`SIM_MAX_LEN=500` 限制超长文本参与比较

### 问题：会话 token 纯内存，服务重启全部掉线

**TL;DR**：token 存 `sessions` Map（内存态），重启即失效需重新进入——多用户工具可接受，但需在文档/UI 说清。

- 问题：服务重启后所有用户被踢回选用户页
- 根因：设计取舍——token 不落盘，避免额外存储与过期管理
- 解决：前端 localStorage 存 `cur`（id/token/name/color），boot 时带 token 恢复会话；401 时 `handleSessionLost()` 自动回选用户页
- 预防：如未来要持久会话，改 users.js 的 createToken/verifyToken，落盘 + 过期扫描，其余层不动

### 问题：文件下载被浏览器执行（XSS 风险）

**TL;DR**：下载一律 `Content-Disposition: attachment` + `application/octet-stream` + nosniff——SVG/HTML 也不会被执行。

- 问题：上传 HTML/SVG 后浏览器可能直接渲染执行脚本
- 根因：静态服务默认按扩展名猜 Content-Type 内联展示
- 解决：上传黑名单（BLOCKED_MIME/BLOCKED_EXT 双保险）拒可执行/脚本类；下载强制 attachment + `X-Content-Type-Options: nosniff`；存储用随机文件名（`<fileId>.<ext>`）防路径穿越
- 预防：下载侧永远是最后防线——attachment 别改 inline；新增上传类型先过黑名单

### 问题：零依赖下解析 multipart/form-data

**TL;DR**：基于 Buffer 字节定位手写解析（`parseMultipart`）——`--boundary` 分割 + 头部/数据切分 + filename 判定文件字段，兼容二进制内容。

- 问题：平台无第三方依赖约束下不能引 formidable 等库
- 根因：Node 内置没有 multipart 解析器
- 解决：`helpers.js parseMultipart`——找 `\r\n--boundary` 切 parts，每 part 切 `\r\n\r\n` 头/体，`filename=` 存在视为文件字段（保留 buffer/mime），否则 utf8 文本
- 预防：文件上传走 `/api/files`（multipart），新增文件类字段沿用此解析器

### 问题：会话 token 纯内存，服务重启全部掉线（v0.3.0 持久化）

**TL;DR**：会话表落盘 `sessions.json`（内存 Map 做缓存 + 文件做持久层），token 带 30 天过期，重启不掉线；改密/删号仍联动销毁。

- 问题：服务重启后所有用户被踢回选用户页，多用户工具体验割裂
- 根因：`sessions` Map 纯内存态（原设计取舍）
- 解决：`ensureSessionsLoaded()` 惰性从文件加载 → 内存缓存；`createToken`/`destroyToken`/`destroyUserSessions` 变更即 `persistSessions()`；`verifyToken` 惰性删过期；server 60s 周期 `pruneExpiredSessions()`
- 预防：token 校验是高频路径（每个 API 请求），**必须保持内存缓存 + 文件兜底**，不要每次请求读文件；过期清理惰性即可，不要每请求写盘

### 问题：滚动归档把新存入条目立即移走（v0.3.1 修复）

**TL;DR**：rollToArchive 按 copyCount 排序保留前 N，新条目 copyCount=0 被当低价值滚进归档——改为按 updatedAt 保留最近更新的。

- 问题：用户存满 500+ 条（带复制次数）后，每存一条新内容立即从列表消失
- 根因：`rollToArchive` 用 `sortClips()`（置顶→复制次数→更新时间）保留前 N，新条目排末尾
- 解决：改按 `updatedAt` 降序保留前 N（刚存入/刚复制/刚编辑的 updatedAt 最新，绝不进归档）
- 预防：归档判定优先级=「用户最近在意的」，不要用"价值排序"（新数据天然低价值）

### 问题：登录限流全局连坐（v0.3.1 修复）

**TL;DR**：限流 key 从 IP 改为 ip:userId，只锁同一 IP 对同一用户的尝试。

- 问题：本机所有请求都是 127.0.0.1，A 输错 8 次后 B/C 全部被锁 60s
- 根因：`loginGuard` 按 `remoteAddress` 记
- 解决：`loginKey(ip, userId)` 组合 key；路由层传 body.id 作为维度
- 预防：限流维度 = 攻击者的"目标资源"，不能只按来源

### 问题：会话失效后列表空白（v0.3.1 修复）

**TL;DR**：handleSessionLost 重置 filter 缺 type/archived 字段 → renderList 里 type=undefined 过滤掉全部条目——改用 resetFilter()。

- 问题：被踢/被删号后重进任何用户，列表全空
- 根因：`state.filter = { q:"", tag:"" }` 缺字段，与 resetFilter 不一致
- 解决：handleSessionLost 调 resetFilter()
- 预防：filter 重置只有 resetFilter 一个入口，禁止手写对象

### 问题：Windows 沙箱下 unlinkSync 抛错但实际已删除（v0.3.1 修复）

**TL;DR**：safe-delete 拦截导致 unlinkSync/rmdirSync 抛 `[safe-delete]` 错误，但文件实际已删——rmForce 逐个 try/catch 吞掉，避免批量清理中断。

- 问题：deleteUser 中途 500，后续 tombstones/webdav 清理不执行
- 根因：rmForce 的 unlinkSync 在沙箱 safe-delete 拦截下抛错（文件实际已删）
- 解决：rmForce 每步 try/catch 吞错
- 预防：删除类工具函数一律容错，删除结果以"文件是否存在"为准而非"是否抛错"

### 问题：条目无限增长，JSON 文件越写越大（v0.2.0 滚动归档）

**TL;DR**：活跃区设上限（500 条），超出后按排序价值保留前 N、最旧移入归档文件——不删数据，单文件可控，搜索默认只查活跃区。

- 问题：用户不设过期则条目永久堆积，users/<uid>.json 无限膨胀，读列表/排序/搜索变慢
- 根因：只有"手动删/过期"两条清理途径，都没有默认保障
- 方案对比：截断删除（丢数据）vs 分片拆分（同步协议重写、读盘量不减）vs **滚动归档**（零丢失 + 改动小）——选归档
- 解决：`saveClips` → `rollToArchive`（排序价值保留前 MAX，其余按 createdAt 升序追加进 `<uid>.archive.json`）；`listClips` 加 `archived` 参数合并归档（标记只读）；清空/删用户/过期清扫全部覆盖归档
- 预防：**同步快照只含活跃区**（归档是本地扩展，协议零改动）；新增容量相关功能先想"丢不丢数据"

### 问题：Set 没有 slice 方法导致导入崩溃（v0.2.0 修复）

**TL;DR**：`sanitizeImported` 里 `new Set(...).slice()` 语法错误（Set 无 slice），导入即崩——改为先展开数组再 slice。

- 问题：POST /api/import 任何数据都报 `Set.slice is not a function`
- 根因：写 `new Set(...).slice(0, N)` 时误以为 Set 是数组
- 解决：`[...new Set(...)].slice(0, N)`
- 预防：Set 转数组必须先展开；导入类新代码写完后用真实 JSON 跑一次端到端

### 问题：点击卡片复制内容也会弹出"存入"大窗口（v0.1.3 修复）

**TL;DR**：卡片复制写剪贴板会触发 `clipboardchange` 监听 → 自动弹存入窗；用"来源抑制时间戳"区分——卡片点击复制时 800ms 内不自动弹。

- 问题：点卡片复制文本/图片后，刚复制完就弹出"存入内容"大窗口，体验割裂
- 根因：`copyText`/`copyImageToClipboard` 写入系统剪贴板 → `clipboardchange` 事件触发 → boot 里的监听器误以为是"外部复制内容"→ `openPasteModal(true)`
- 解决：全局 `suppressAutoPasteUntil` 时间戳——`card.onclick` 入口先置 `Date.now() + 800`，`clipboardchange` 回调开头检查 `Date.now() < suppressAutoPasteUntil` 则跳过自动弹窗
- 预防：所有"监听剪贴板变化做自动化"的逻辑，必须先排除"本应用自己写剪贴板"的路径（来源抑制），否则自我触发

### 优化：内容去重确认（前端）

**TL;DR**：存入时若文本/链接内容已存在，弹确认"仍要存入？"——不打断文件条目，防误存重复。

- 问题：同一内容反复粘贴存入，列表冗余
- 根因：无重复感知
- 解决：openPasteModal 保存前 `state.clips.some(c => url===content || content===content)` 命中则 askConfirmP 确认
- 预防：重复检测只针对文本/链接（文件有 fileId 天然唯一），不打断文件流程

## 开发记录

- 2026-08-09（init）：多用户剪贴板工具——万能入口/智能排序/拼音搜索/标签/WebDAV 备份同步；提交 `007293a`
- 2026-08-09（chore）：添加 API 部署脚本（github.com 被墙时走 Git Data API）；提交 `b21a6eb`
- 2026-08-12（docs）：按单项目规范补齐四件套（AGENTS / DEVELOPMENT / CHANGELOG），README 校对
- 2026-08-12（v0.1.3）：修复卡片复制触发自动弹窗（suppressAutoPasteUntil 来源抑制）
- 2026-08-12（v0.2.0）：P0 四件套——星标收藏 / 滚动归档 / 导出导入 / URL 清理
- 2026-08-12（v0.3.0）：P1 三项——会话持久化（sessions.json 落盘）/ 标签管理（重命名删除）/ JSON 格式化预览
- 2026-08-12（v0.3.1）：修复模拟执行检查 7 问题——归档保留策略/限流 key/filter 重置/删用户残留/rmForce 容错/空标签/标题 utm
- 2026-08-12（v0.4.0）：前端 JS 拆 public/app.js（index.html 1251→125 行）+ 图片卡片缩略图/预览按钮/hover 浮层
- 2026-08-12（v0.4.2）：图片预览完善（默认100%/缩放/拖拽/步长可调）+ 触发逻辑重构（先比对再弹窗）+ 弹窗/入口重构（密码/数据管理/标签管理/一键同步/复制提示定位/完整时间）
- 2026-08-12（v0.4.3）：架构评估 v2 三项落地——hover 状态显式化（previewState）/ openDataModal 拆 WebDAV 区（renderWebdavSection）/ 重复检测抽纯函数（findDuplicateClip）
- 2026-08-12（v0.4.4）：函数粒度拆分（架构评估 v3 量化落地）——clipCard CC49→15（make*Btn 工厂 + bindImageHoverPreview）/ openPasteModal CC46→22（savePasteContent + autoFillPasteModal），app.js 无 CC>22
- 2026-08-12（v0.4.5）：逻辑核验加固（状态图验证方法论）——3 处异步补 guard（logoutBtn/archChk/用户卡 enterUser）+ findDuplicateClip 字段兜底
- 2026-08-12~13（v0.5.0-设计稿）：主页面整体 UI 重构设计稿（design-preview.html）——常用优先分区直达/等高网格/渐进式披露卡片/磨砂玻璃深色系/5 磨砂弹窗/极窄顶栏 ⋯ 菜单。**未落地**。⚠️ **2026-08-14 已废弃**：UI 重构确定不做，设计稿已删除（v0.5.2）
- 2026-08-14（v0.5.1-P0 修正批）：补 package.json（type:module + engines + npm scripts）；测试脚本参数化（TEST_PORT/TEST_DATA_DIR）；mock-webdav safeJoin 路径修复（path.resolve 统一两侧，相对路径不再 400）；safe-delete 根源澄清（WorkBuddy shim 注入，非 Node 原生）；README .data 纠偏
- 2026-08-14（v0.5.2）：取消 UI 重构，删除 design-preview.html 设计稿
- 2026-08-15（v0.6.0）：**暗黑新拟态 UI 迁移**（依据《UI迁移手册.md》：双阴影浮雕/去边框/分段控件/新拟态开关/主色粉→蓝紫；修复 transform 劫持 fixed 定位的图片预览浮层"飘远" bug）+ **富文本双格式复制**（条目 `html` 字段 ≤512KB、`copyRich` 双 MIME 写剪贴板、卡片 🅡 按钮；决策不上数据库——JSON 规模足够且保零依赖）+ 新增 `cc-measure.mjs`/`test-html-field.mjs`

## 测试

- `scripts/smoke-test.mjs`：API 冒烟 34 项（用户/条目/文件/搜索/隔离/安全/改密）——需独立数据目录实例
- `scripts/test-webdav-sync.mjs`：WebDAV 端到端 19 项（墓碑传播/清空恢复/实体同步）——需 mock WebDAV 8180
- `scripts/test-auto-sync.mjs`：自动同步（间隔到期触发）
- `scripts/mock-webdav.mjs`：极简 WebDAV 服务器（MKCOL/PUT/GET/DELETE + Basic admin:admin123）
- `scripts/test-html-field.mjs`：富文本 html 字段单测 10 项（新建/截断/更新/清空/导出/导入）——无需起服务
- `scripts/cc-measure.mjs`：圈复杂度/认知复杂度/LOC 测量（AST 级 tokenizer）

**注意**：webdav 测试固定用户名非幂等，重跑前清空测试数据目录（见 AGENTS.md 关键坑）。
