# DEVELOPMENT.md · 开发文档

## 项目概览

clipboard-tool 是 tools-center 平台的一个**多用户剪贴板管理工具**：粘贴自动识别（URL→链接 / 其他→文本 / 图片文件→文件条目）、一键复制、双击编辑、标签体系、拼音首字母搜索、智能排序（复制次数→标签相近→内容相似）、可选密码锁、WebDAV 备份同步。Node 22+ 零依赖，JSON 文件存储，双模式运行（平台托管 / 独立运行）。

## 架构说明

```
server.mjs (入口薄层:静态服务 + 路由分发 + 过期清扫 60s + 自动同步 60s)
  ├── lib/core/        纯业务逻辑（不碰 HTTP）
  │   ├── config.js    全局配置单一来源（端口/输入边界/上传黑名单/UUID 白名单）
  │   ├── store.js     JSON 原子读写 + assertId + httpError + rmForce
  │   ├── clips.js     条目域：CRUD + 复制计数 + 排序/搜索/标签 + 墓碑 + 过期清扫
  │   ├── users.js     用户域：scrypt 密码 + 内存会话 token + 登录限流
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
- `users/<uid>.json`：每人条目数组
- `users/<uid>.tombstones.json`：每人墓碑（删除传播记录，90 天 TTL）
- `users/<uid>.webdav.json`：每人 WebDAV 配置（含明文密码）
- `files/<uid>/<fileId>.<ext>`：文件实体（随机名防路径穿越）

**排序（服务端单一实现）**：① copyCount 降序 → ② 标签相近归拢（Map 倒排）→ ③ 内容相似归拢（10 字符 ngram 倒排索引，O(n×L) 非 O(n²)）。读取时计算，任何修改即时反映。

## 关键问题与方案

### 问题：Windows 上 Node 22 `fs.rmSync` 抛 safe-delete 错误

**TL;DR**：Node 22 Windows 的 `rmSync` 走回收站（safe-delete）机制，部分路径下抛 `[safe-delete] 操作失败: Some operations were aborted`——删除一律用自写 `rmForce()`。

- 问题：删除用户数据/文件实体时 `fs.rmSync(path, { recursive: true, force: true })` 偶发抛错
- 根因：Node 22 引入的 safe-delete 回收站机制与某些路径/权限组合不兼容
- 解决：`lib/core/store.js` 实现 `rmForce()`——stat 判断类型后手动 `readdir + 递归` 或 `unlink`，确定性删除、不存在静默
- 预防：项目内所有删除路径（删用户/删文件/过期清扫）统一走 `rmForce`，禁止直接用 fs.rmSync

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

## 测试

- `scripts/smoke-test.mjs`：API 冒烟 34 项（用户/条目/文件/搜索/隔离/安全/改密）——需独立数据目录实例
- `scripts/test-webdav-sync.mjs`：WebDAV 端到端 19 项（墓碑传播/清空恢复/实体同步）——需 mock WebDAV 8180
- `scripts/test-auto-sync.mjs`：自动同步（间隔到期触发）
- `scripts/mock-webdav.mjs`：极简 WebDAV 服务器（MKCOL/PUT/GET/DELETE + Basic admin:admin123）

**注意**：webdav 测试固定用户名非幂等，重跑前清空测试数据目录（见 AGENTS.md 关键坑）。
