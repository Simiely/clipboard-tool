# CHANGELOG.md

## v0.6.4 (2026-08-15)

### 主页面版式改版 · 双行工具栏（方案 13 落地）

- 🧰 **工具条重设计**（`renderMain` + index.html 主页面 CSS）：
  - **行1**：大搜索胶囊 + **右侧金色小按钮「＋存入」**（存入入口收缩为小按钮，点击打开存入大弹窗；粘贴检测自动弹窗不受影响）
  - **行2**：类型分段（全部/文本/链接/文件）+ 标签栏（含管理按钮）+ 右侧操作（**含归档开关 / 列数选择 / 「↻ 同步」**）
- 顶栏精简：一键同步从顶栏移入工具行 ops；密码 / 数据管理 / 退出保留
- 删除旧的 `.paste-trigger` 大条与 `.toolbar` 样式；`.tagbar`/`.typetab` 改为行内无 margin
- `renderTagbar` 增加容器参数（渲染进 `.tagbar-wrap`；无参自动查找，兜底 `#view`），`refreshList` 等调用点同步
- **零功能变更**：搜索防抖 / 类型过滤 / 标签过滤管理 / 含归档 / 列数偏好 / 一键同步 / 存入弹窗全部保留
- 验证：语法通过 · 冒烟 34/34 ✅

## v0.6.3 (2026-08-15)

### 首页改版 · 极简墙布局（方案 C 落地）

- 🏠 **用户选择页整体重设计**（`renderUserSelect` + index.html 用户区 CSS）：
  - 品牌区：印章式「剪」logo（内嵌方块+金色）+ 衬线标题「剪贴板」（Noto Serif SC 栈）+ 副标 `SELECT A PERSON · 选择身份进入`
  - 用户网格：紧凑卡（44px 头像 + 名字 + 计数），`auto-fill minmax(150px)` 高密度铺排；hover 金色描边 + 凸起阴影，active 内凹
  - 新建用户：虚线占位卡（+ 图标），hover 金描边
  - 底部操作：「编辑」胶囊按钮（on 态内凹金字），编辑模式删除按钮淡入显示
  - 脚注：`LOCAL JSON · 数据隔离`
- **零逻辑改动**：功能/事件/编辑模式/新建弹窗全保留，仅换 DOM 结构 + 样式；`setUserEditMode`/boot 空白点击委托选择器未变
- 新增令牌：`--ease` 指数缓动；`.serif` 衬线工具类
- 验证：语法通过 · 冒烟 34/34 ✅

## v0.6.2 (2026-08-15)

### 酒红金配色方案（暗黑新拟态 · 奢华暗黑调 · 校准版）

- 🎨 **对齐 dark-design-style-guide 主页示意 mock**（`--mbg:#1A1A1A / --msurf:#2F2F2F / --mtext:#DADADA / --mmuted:#848484 / --mac:#C9A96E / --mac2:#AE4D4D / --mat:#101014`）
- 设计令牌重设：背景 `#1A1A1A` / 面板 `#2F2F2F` / 内嵌 `#141414` / 文字 `#DADADA` / 次要 `#848484`（**中性黑灰底**，非酒红暖调）
- 主强调 `#C9A96E` 金（按钮/选中/选区/标签 active，**深字 `#101014` 配**）、点缀 `#AE4D4D` 砖红（链接徽章/危险 hover/链接文字 hover）
- 阴影高光：**中性灰** `rgba(88,88,88,.3-.4)`（对齐中性底，非暖棕）
- 功能色：绿 `#9FBF8F`（文本徽章）/ 金 `#D4AF37`（文件徽章）/ **砖红 `#AE4D4D`（链接徽章，与 mac2 统一）**/ 暖红 `#E08A7A`（危险）
- 用户头像色板：金为主 + 砖红点缀 + 暖棕系（`users.js PALETTE`）
- **零逻辑改动**：仅 `index.html` 令牌与 `users.js` 色板，app.js 无硬编码颜色、行为不变

> 调整记录：首版酒红主强调 → 二版金主红点缀 → 三版对齐 mock 中性黑灰底（金主 + 砖红点缀）

## v0.6.1 (2026-08-15)

### 针对性走查修复批（P-101~P-104）

- 🔴 **P-101 删除失败误报"已删除"修复**：`makeDeleteBtn` 增加 `if (r)` 返回值守卫——API 失败（网络/会话失效）时仅 errToast，不再无条件 `flash("已删除")` 误导用户；与删用户/删标签/置顶/JSON 覆盖的检查风格对齐
- 🟡 **P-102 墓碑仅在配置 WebDAV 后记录**：`deleteClip` 改为返回 `{ ok, fileId, tombstone }` 不落盘，路由层 `getSyncConfig` 存在才调 `recordTombstone`——未配置同步的用户删除不再产生 `<uid>.tombstones.json` 文件（同步语义不变，测试覆盖保持）
- 🟡 **P-104 自动同步失败可观测**：`runAutoSync` 失败时把 `lastSyncError` 写入 `<uid>.webdav.json`（不更新 lastSyncAt，保持原"每周期重试"节奏），`GET /api/sync/config` 返回该字段，前端数据管理弹窗展示「⚠ 上次自动同步失败:xx」
- ⚪ **P-103 拼音搜索范围文档注明**：README 功能列表注明"拼音匹配仅前端搜索框输入生效；后端 `?q=` 为子串匹配"（现状确认，非缺陷）

### 验证
- 冒烟 34/34 ✅ · WebDAV 集成 19/19 ✅ · 自动同步 3/3 ✅
- P-102 实证：未配置 WebDAV 的用户删除后无墓碑文件、条目正常删除
- P-104 实证：mock 远端停机 → 自动同步失败 → `lastSyncError="WebDAV 连接失败: fetch failed"` 落盘并由 GET 接口返回，lastSyncAt 保持原值

## v0.6.0 (2026-08-15)

### 暗黑新拟态 UI 迁移 + 富文本双格式复制

**🎨 UI 迁移（暗黑新拟态 Neumorphism，依据《UI迁移手册.md》）**
- 设计令牌全换：背景 `#1c1f26` / 面板 `#232730` / 内嵌 `#191c22`，主强调色粉 `#ff9292` → 蓝紫 `#7f9dff`
- 风格核心：**双阴影浮雕**（凸起=外阴影、凹陷=内阴影、按压=阴影反转），全界面去 1px 描边；阴影偏移统一 1px（贴合感），圆角 14-18px
- 组件改造：按钮四态、输入框凹陷内嵌、类型 Tab 改分段控件（凹陷轨道+凸起选中）、checkbox 伪装新拟态开关、搜索框胶囊+放大镜图标
- 布局对齐：容器 720→960px、hero 紧凑化、卡片间距 16px、顶栏整条浮雕面板
- **关键修复**：`.clip-card` hover/active 去掉 `transform`——卡片内部挂 `position:fixed` 的图片预览浮层，transform 会创建 containing block 使 fixed 相对卡片而非视口，导致浮层定位错乱（浮层"飘远"根因）
- 图片 hover 预览浮层与卡片间隙 8px → 4px（贴合卡片）

**📋 富文本双格式复制（content 纯文本 + html 富文本并存）**
- 后端：条目新增可选 `html` 字段（`MAX_HTML: 512KB`）；创建/编辑/导入净化全覆盖，导出/WebDAV 天然携带
- 前端：存入时检测剪贴板 `text/html` → 有富文本来源则存双版本；卡片显示 **🅡 富文本复制按钮**（仅富文本条目）
- 复制：`copyRich()` 剪贴板写入双 MIME（`text/html` + `text/plain`）——粘贴到 Word/飞书保留格式、记事本得纯文本；execCommand 降级兜底
- 决策：**不上数据库**（JSON 足够：100 用户×500 条×512KB 上限；上库破坏零依赖 + WebDAV 快照协议；`node:sqlite` 仍实验性）

**🛠 工具与测试**
- 新增 `scripts/cc-measure.mjs`：AST 级圈复杂度/认知复杂度/LOC 测量工具
- 新增 `scripts/test-html-field.mjs`：富文本 html 字段单测（新建/截断/更新/清空/导出/导入）
- 验证：html 字段单测 10/10、冒烟 34/34、WebDAV 19/19、自动同步 3/3 全绿

## v0.5.2 (2026-08-14)

### 取消 UI 重构，删除设计稿（产品决策）

- ❌ **删除 `design-preview.html` 设计稿**：v0.5.0 的主页面 UI 重构方案（磨砂玻璃/等高网格/渐进披露卡片）**确定不做**，设计稿文件已从仓库移除（`git rm`）
- 影响：前端维持 v0.4.x 现有界面（index.html + app.js 不变）；CHANGELOG v0.5.0-设计稿 章节保留仅作历史记录（标注已废弃）
- 无代码改动，纯清理

## v0.5.1 (2026-08-14)

### P0 修正批：工程卫生 + 文档纠偏（优化计划落地）

- 🟡 **补 package.json**（P0-1）：`"type": "module"`（显式声明 ESM，消除 Node 22 模块语法探测开销）+ `engines.node >=22.7` + npm scripts（`start` / `smoke` / `test:webdav` / `test:auto-sync` / `test` 一键三套）
- 🟡 **测试脚本参数化**（P0-2）：`test-webdav-sync.mjs` / `test-auto-sync.mjs` 的端口与数据目录改读 `TEST_PORT`（默认 8131）/ `TEST_DATA_DIR`（默认 `C:/Temp/clipboard-test`），与 smoke-test 风格统一，不再硬编码
- 🔴 **mock-webdav safeJoin 路径修复**（P0-3）：`path.join`（相对路径无盘符）与 `path.resolve`（补盘符）不一致导致相对路径下 `startsWith` 恒 false、所有请求 400 bad path——两侧统一 `path.resolve` 归一化，且修复前缀匹配边界（`dataDir + path.sep` 精确判定），`../` 逃逸仍拦截
- 🟡 **safe-delete 根源澄清**（P0-4）：实测确认 `fs.rmSync.name === "wrappedRmSync"` 是 **WorkBuddy AI 沙箱注入的 genie-safe-delete shim**（NODE_OPTIONS 猴补丁送回收站），非 Node 22 原生行为；用户手动运行正常。AGENTS/DEVELOPMENT 关键坑重写，rmForce 容错设计不变
- 🟡 **README 纠偏**（P0-4）：数据目录 `./data` → `./.data`；测试命令补 Windows 绝对路径说明

### 验证
- 冒烟 34/34 无回归；WebDAV 集成 19/19 无回归；自动同步 3/3 无回归；mock-webdav 相对/绝对路径 + 逃逸用例全部通过

## v0.5.0-设计稿 (2026-08-12~13) — ⚠️ 已废弃（2026-08-14 决定不做，design-preview.html 已删除）

### 主页面整体 UI 重构设计稿（未落地代码，先评审）

- 🟡 **design-preview.html 设计稿**（纯静态可预览）：
  - **范式**：常用优先·分区直达（置顶/最近分区，0 输入点击即复制，搜索兜底）——参考 Chrome 剪贴板历史 / Win+V
  - **布局**：等高网格（放弃瀑布流参差）→ 大屏 auto-fit 多列自适应 → 窄屏双列/单列
  - **卡片 v3**：渐进式披露（默认干净，hover 浮现操作图标）；类型色点替代文字标签；置顶仅星标徽章不整卡高亮；操作按钮按类型匹配程序（链接=打开/图片=下载/全=编辑+删除）
  - **磨砂玻璃**：深色为主（背景 #101014 + 光斑 opacity .07-.14 + 玻璃填充 85-92%），粉色仅点缀
  - **弹窗体系**：存入/编辑(含重复提示)/密码/数据管理/标签管理 5 个磨砂弹窗，底部切换条演示；过期改 chips 分段选择（统一设计语言）
  - **极窄顶栏**：<520px emoji-only 按钮 + ⋯ 收起菜单（低频收菜单、高频保留）
- ⚠️ **状态**：设计稿已生成待评审，**未合入正式代码**（index.html CSS 与 app.js 未动）

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
