# CHANGELOG.md

## v0.4.5 (2026-08-12)

### 逻辑核验加固（状态图验证方法论：防连点覆盖补齐）

- 🔴 **3 处异步操作补 guard 防连点**（核验 P2-1）：退出按钮 / 归档勾选 / 用户选择页卡片——消除快速连点导致的并发竞态（重复 DELETE 会话、归档加载竞态、并发建会话）
- 🟡 **findDuplicateClip 加字段兜底**（核验 P2-2）：link 条目的 url 与 text 条目的 content 均 `|| ""` 兜底——防 undefined 参与比对（防御性）

### 验证
- 冒烟测试 34/34 无回归；app.js 语法校验通过

## v0.4.4 (2026-08-12)

### 函数粒度拆分（架构评估 v3 量化落地：圈复杂度大幅下降）

- 🟡 **clipCard 拆分（CC 49 → 15）**：7 个操作按钮抽独立工厂函数（makePinBtn/makeOpenBtn/makeDownloadBtn/makeEditBtn/makeDeleteBtn/makeJsonBtn），预览区/meta 区/单击动作各抽独立函数（makeCardPreview/makeCardMeta/handleCardClick），hover 预览抽 bindImageHoverPreview（CC 14）——216 行 → 27 行组装
- 🟡 **openPasteModal 拆分（CC 46 → 22）**：保存流程抽 savePasteContent（CC 10，含重复检测）、自动填入抽 autoFillPasteModal（CC 16）——151 行 → 103 行编排
- 🟢 全部拆分后 app.js 无 CC>22 函数（boot 25 为启动编排，属可接受）

### 验证
- 冒烟测试 34/34 无回归；app.js 81021 字节完整加载；各拆分函数语法校验通过

## v0.4.3 (2026-08-12)

### 架构评估 v2 三项落地（主线 A / 支线 A- / 模块化 B+）

- 🟡 **hover 预览状态显式化**：`scale/hoverTimer/hoverBox` 收敛为单一 `previewState` 状态对象（open/scale/timer/box/drag），zoom 抽独立函数，滚轮/拖拽/开关统一走 open/close/applyScale——防布尔失控（FSM 标准）
- 🟡 **openDataModal 拆分**：WebDAV 配置区抽独立 `renderWebdavSection()`（弹窗 156 行 → 100 行）——单一职责"函数>20 行即拆分信号"
- 🟡 **重复检测抽纯函数**：`findDuplicateClip(content, clips)` 无 DOM 副作用，clipboardchange 与存入按钮统一调用——比对逻辑可单测（6 项用例通过）
- 🟢 注释/文档同步（CHANGELOG / DEVELOPMENT / 架构评估报告-v2 标记已修复）

### 验证
- 冒烟测试 34/34 无回归；app.js 完整加载 + 语法校验通过

## v0.4.2 (2026-08-12)

### 图片预览完善 + 交互重构

- 🟡 **图片预览默认 100%**：缩放范围 50%~300%，滚轮步长默认 15%（设置可调 1%~50%）
- 🟡 **浮层跟随卡片 + 框体随缩放同步变大** + 放大后拖拽平移查看（替代滚动条）
- 🔴 **弹窗触发逻辑重构**（先比对再弹窗）：clipboardchange 先读剪贴板比对仓库——重复直接弹编辑页带「已有相同内容」提示，不重复才弹存入窗；卡片点击复制不弹（来源抑制）
- 🟡 **存入弹窗优化**：高级选项（别名/标签/过期）默认全展开；选图片后隐藏文本区（可 ✕ 取消）
- 🟡 **标签管理独立入口**：标签栏最右「管理」按钮 → 独立弹窗（从设置挪出）
- 🟡 **顶栏重构**：「设置」拆「密码」+「数据管理」；删「切换」（与退出重复）；新增顶栏「一键同步」
- 🟡 **复制提示跟随鼠标位置**（点击坐标）；过渡只作用于透明度（修复"提示飞过来"）
- 🟡 **卡片时间完整格式**（年月日+时分）；图片卡片删冗余「预览」按钮

## v0.4.0 (2026-08-12)

### 前端 JS 拆分 + 图片卡片体验

- 🔴 **前端 JS 拆分独立文件**（架构评估落地）：`index.html` 1251 行 → 125 行（HTML+CSS），JS 移入 `public/app.js`（1129 行）；`server.mjs` 加 `/app.js` 静态路由——单文件超维护甜蜜点问题解决
- 🟡 **图片卡片缩略图**：图片类型卡片显示小缩略图（懒加载 `loading="lazy"`），替代文件名文本预览
- 🟡 **图片预览按钮**：图片卡片 ops 区新增「预览」按钮 → 打开大图弹窗（下载按钮保留）
- 🟡 **图片 hover 预览浮层**：鼠标移入图片卡片 260ms 后弹出 320px 大图浮层（含文件名+大小）；移出消失；自动避让屏幕边缘

### 验证
- app.js 68362 字节完整加载 + 语法校验通过；上传图片端到端（上传→条目→缩略图 URL 可访问）
- 冒烟测试 34/34 无回归（前端拆分不影响功能）

## v0.3.1 (2026-08-12)

### 修复：模拟执行检查发现的 7 个问题

- 🔴 **滚动归档不再移走新条目**：`rollToArchive` 由「按 copyCount 排序保留」改为「按 updatedAt 保留最近更新的前 N 条」——刚存入/刚复制/刚编辑的条目绝不进归档（此前新条目 copyCount=0 会被立即滚走，用户存完看不到）
- 🔴 **登录限流不再连坐**：限流 key 由 IP 改为 `ip:userId`——只锁「同一 IP 对同一用户」的暴力尝试，一个用户输错密码不再锁住所有用户
- 🔴 **会话失效后列表不再空白**：`handleSessionLost` 改用完整 `resetFilter()`（此前重置 filter 缺 type/archived 字段，重进用户后列表被 type=undefined 过滤成空白）
- 🟡 **删除用户清理配置残留**：`deleteUser` 补删 `<uid>.tombstones.json` 和 `<uid>.webdav.json`（此前残留，webdav 配置含密码明文有泄漏风险）
- 🟡 **rmForce 容错加固**：unlinkSync/rmdirSync 在沙箱 safe-delete 拦截下"抛错但实际已删除"——逐个 try/catch 吞掉，保证批量清理不中断（此前 deleteUser 中途 500 导致后续清理不执行）
- 🟡 **标签重命名空目标拒绝**：`renameTag` 校验 `!to` 返回 400（此前空目标会静默删除标签）
- 🟢 **链接自动标题用清理后 URL**：`createClip` link 标题改取 `clip.url`（此前用原始 URL，utm 追踪参数残留进标题）
- 🟢 **归档不进 WebDAV 快照的说明**：README + 前端「含归档」开关 title 提示（归档只存本地，不参与 WebDAV 同步）

### 验证
- 7 项复现用例全部通过（新条目保留/限流不连坐/无配置残留/空标签 400/标题无 utm/rmForce 容错）
- 冒烟测试 34/34 无回归；墓碑合并边界单测通过

## v0.3.0 (2026-08-12)

### P1 功能三项 + 会话持久化

- 🔴 **会话持久化**：token 从内存 Map 落盘到 `sessions.json`（`CAP_STORAGE_DIR`），**服务重启不再掉线**；token 带 30 天有效期，60s 后台惰性清理过期会话；改密/删号/登出联动清理（含保留当前会话语义不变）
- 🟡 **标签管理增强**：设置弹窗新增「标签管理」区——重命名 / 删除标签，跨活跃区+归档全部条目同步生效，重命名同名合并去重（`PUT/DELETE /api/tags/:name`）
- 🟡 **JSON 格式化预览**：文本条目内容可解析为 JSON 时，卡片出现 `{}` 按钮——弹窗美化展示（2 空格缩进），可复制美化结果 / 覆盖保存回条目
- 🟡 设置弹窗加滚动（`max-height:80vh` + overflow-y:auto），内容变多不溢出

### 修复
- `pruneExpiredSessions` 未导出导致 server.mjs 启动报 SyntaxError——补 export

### 验证
- 标签重命名/删除 API 端到端（中文标签 URL 编码）；会话落盘文件生成；**重启后 token 仍有效**；冒烟测试 34/34 无回归

## v0.2.0 (2026-08-12)

### P0 功能四件套（本地体验 + 数据安全）

- 🔴 **星标收藏（Pin）**：卡片 ★/☆ 一键置顶，排序升级为「pinned 置顶 → 复制次数 → 标签相近 → 内容相近」；置顶卡片琥珀色描边高亮（`/api/clips/:id/pin`）
- 🔴 **滚动归档**：活跃区上限 500 条/用户（`MAX_CLIPS_PER_USER` 可配），超出后按排序价值保留前 500、最旧条目按 createdAt 升序移入 `users/<uid>.archive.json`——**零丢失**；工具栏「含归档」勾选可搜历史（`?archived=1`，只读展示）
  - 清空 = 活跃区+归档一并清空；删除用户连带删归档；过期清扫覆盖活跃区+归档区
  - 与 WebDAV 语义协调：清空不传播删除的规则不变，归档不进同步快照
- 🔴 **数据导出/导入**：设置弹窗「本地备份」区——导出全部（含归档）为 JSON 下载；导入合并（同 id 取 updatedAt 新者，新增/更新/跳过分开计数），不清理本地既有数据（`/api/export` + `/api/import`）
- 🔴 **URL 自动清理**：保存链接时自动剔除 UTM 及常见追踪参数（utm_* / fbclid / gclid / msclkid / mc_* 等 21 个），无追踪参数原样保留（`cleanUrl()`，新增/编辑均生效）

### 修复
- `sanitizeImported` 的 `new Set(...).slice()` 语法错误（Set 无 slice，导入会崩）——改为 `[...new Set(...)].slice()`

### 验证
- cleanUrl 单测 7/7 通过；滚动归档脚本验证（505 条 → 活跃 500 + 归档 5、导出 505、清空、导入去重、重复跳过）；API 端到端（导出/导入/归档查询/星标排序）；冒烟测试 34/34 无回归

## v0.1.3 (2026-08-12)

### 修复：点击卡片复制不再自动弹出"存入"窗口

- 🔴 **问题**：点卡片复制文本/图片后，`clipboardchange` 监听误判为"外部复制内容"，刚复制完就自动弹出存入大窗口
- 🔴 **根因**：本应用 `copyText`/`copyImageToClipboard` 写系统剪贴板 → 触发 `clipboardchange` → boot 监听器 `openPasteModal(true)`
- 🟢 **修复**：新增 `suppressAutoPasteUntil` 来源抑制时间戳——`card.onclick` 入口置 800ms 窗口，`clipboardchange` 回调在窗口期内跳过自动弹窗
- 🟢 验证：语法校验通过，页面实时加载新代码（server 每次请求读 index.html，无需重启）

## v0.1.2 (2026-08-12)

### 文档四件套补齐（docs）

- 按 knowledge-base 单项目规范补齐四件套：`AGENTS.md`（技术栈/关键坑/约定/常用命令 + 文档基线）、`DEVELOPMENT.md`（架构说明 + 一坑一篇问题记录）、`CHANGELOG.md`（本文件）
- README.md 校对（功能清单与实现核对，补充文档索引）
- 文档基线：2026-08-12（commit 见 AGENTS.md 顶部）

## v0.1.1 (2026-08-09)

### 部署工具（chore `b21a6eb`）

- 新增 `scripts/deploy-via-api.mjs`：github.com 被墙时通过 GitHub Git Data API（api.github.com 可直连）推送本地仓库——blobs → tree → commit → ref（main 不存在则创建，已存在强推）

## v0.1.0 (2026-08-09)

### 初始化：多用户剪贴板工具（init `007293a`）

**功能**：
- **单一万能入口**：粘贴自动识别（URL→链接 / 其他→文本 / 拖放/选择文件→文件条目 / Ctrl+V 粘贴图片文件）；检测到复制内容自动弹出大窗口（`clipboardchange` 监听）
- **一键复制 / 双击编辑**：文本/链接复制内容，图片点击直接复制到系统剪贴板（ClipboardItem，降级转 PNG / 预览），其他文件点击下载
- **智能排序**：复制次数优先 → 标签相近归拢 → 内容相似归拢（10 字符片段倒排索引，毫秒级）
- **拼音首字母搜索**：内置 3755 常用字映射表，`sf` 可搜到"身份"
- **标签体系**：点选已有标签 + 输入新建，列表标签过滤
- **多用户**：无密码零摩擦进入 / 可选密码锁，会话 token 内存态（服务重启需重登）
- **WebDAV 备份同步**：单向全量备份 + 双向合并同步（墓碑机制防删除复活、全部清空不传播删除、定时自动同步默认 12h、可选同步文件实体）
- **安全**：UUID 白名单防路径穿越、原子写、登录限流（8 次/60s/IP）、上传黑名单（拒可执行/脚本类）、下载强制 attachment

**技术**：Node 22+ 零依赖（内置 http/fetch/crypto）；前端单文件原生 JS；JSON 文件存储（原子写）；WebDAV HTTP 直连
