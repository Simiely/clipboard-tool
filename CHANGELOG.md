# CHANGELOG.md

## v0.6.16 (2026-08-31)

### 排序修复：严格按点击次数排序（移除标签/内容归拢）

- 🔴 **修复卡片不按点击次数排序**（clips-query.js / clips-store.js）：`listClips` 排序管道原为 `sortClips → groupByTags → groupSimilar`——`sortClips` 按 copyCount 排好后，标签/内容归拢会把共享标签或相似内容的条目拉到一起，导致点击次数高的卡片被挤到低次数卡片后面（实测 3 次点击的卡片排在 4 次点击的前面）
- 🟢 **排序策略定稿**：① pinned 置顶 → ② copyCount 降序 → ③ updatedAt 降序。`listClips` 直接 `sortClips(list).map(publicClip)`，点击次数越高越靠前，严格全局单调
- 🟢 **删除死代码**：`groupByTags` / `groupSimilar` 及其 ngram 倒排索引逻辑整体移除（仅此一处调用）
- ✅ 验证：排序行为验证（pinned 置顶 / copyCount 降序 / 同次数按 updatedAt）+ 冒烟测试无回归

## v0.6.15 (2026-08-28)

### 批量编辑：多选 + 批量删除 / 批量加标签 / 批量减标签

- 🟢 **批量编辑模式**（app.js/index.html）：工具行新增「编辑」按钮进入多选——卡片左上角出现勾选框、整卡点击=切换选择（透明覆盖层，禁用复制/双击编辑/图片 hover 预览）；底部悬浮批量条显示「已选 N 项」+「全选当前页 / 取消全选」+「＋加标签 / －减标签 / 🗑删除 / 完成」
- 🟢 **全选当前页**：抽取 `getVisibleClips()` 纯函数（`renderList` 与「全选当前页」共用同一份可见集定义），全选=当前搜索/标签/类型过滤出的可见条目；已全选则再点取消全选
- 🟢 **后端批量接口**（`POST /api/clips/batch`）：单入口按 `action` 分发 `delete / addTags / removeTags`——`batchDeleteClips` 跨活跃区+归档删除（记墓碑 + 联动文件实体清理，与单条删除同语义）；`batchSetTags` 跨区加/减标签，**刷新 `updatedAt`**（WebDAV 合并的 key，不刷新则改动不同步远端、也不反映到排序）；空 ids / 未知 action 返回 400
- ✅ 验证：冒烟 42/42（新增批量加/减标签、批量删除、空选择/未知操作 400 共 8 条）· merge 17/17 · html 10/10 · playwright 端到端 13/13（进批量→点卡选中→全选→加标签→减标签→删除→退出）
- 🟢 **favicon**（index.html）：📋 emoji 内嵌 SVG data-URI 设为网站图标（`<link rel="icon">`，尖括号编码兼容严格解析器；零额外文件，平台托管/独立运行均生效）
- 🟢 **宽屏自适应**（index.html）：容器 `.view`/`.wall` 基础 960px → `≥1280px` 放宽 1440px → `≥1920px`(4K) 放宽 1920px——卡片墙 auto-fill 多列铺开，充分利用宽屏宽度；1024 以下保持原宽度不回归
- 🟢 **空格快捷键存入**（app.js）：主页面按**空格** = 点击「存入」按钮（打开存入弹窗）。守卫：未登录不触发 / 已有弹窗不叠加 / 焦点在输入框（搜索框打字空格）不拦截 / 仅主页面生效；`preventDefault` 阻止空格滚动页面；Esc 仍关闭弹窗。验证 6/6（弹窗出现/Esc关闭/输入框不误触/不叠加/不滚动）

## v0.6.14 (2026-08-27)

### 架构整理：前端过滤单轨化（bug 修复）+ 墓碑规则下沉 + 模块化拆分

- 🔴 **前端过滤单轨化修复**（app.js `loadClips`）：搜索/标签过滤统一走前端 `renderList`——`loadClips` 去掉 `q/tag` 参数恒拉全量（仅保留 `archived`）。修复真 bug：搜索词非空时发生增删改 → `refreshList` 把 `state.clips` 覆盖成过滤集 → 清空搜索词后全量从界面"消失"（须刷新页面恢复）。根因：后端过滤（历史遗留）与前端过滤（设计意图）双轨并存，一份 state 被两处过滤。后端 `listClips` 的 q/tag 参数保留（API 兼容）
- 🟢 **墓碑规则下沉到 core**（webdav.js `recordTombstoneIfConfigured` + routes/clips.js）："已配置 WebDAV 才记墓碑"原是业务规则写在路由层（`if (r.tombstone && getSyncConfig)`），下沉为 webdav 域内部判断，路由层只做无条件编排调用——消除唯一一处业务规则泄漏到 HTTP 层
- 🟢 **clips.js 585 行拆 5 文件**（clips-store/mutate/query/transfer/tombstones）：按"底层存取 / 写操作 / 查询 / 导入导出 / 墓碑"拆分，依赖方向单向（上层 → clips-store → store/config）零循环；`clips.js` 变纯聚合 re-export，**对外 import 路径与行为全不变**（webdav.js/测试脚本零改动）。renameTag/deleteTag/clearAll/sweep 原直写 `writeJson` 改为 `saveClips/saveArchive`（语义一致，超限滚动更正确）
- 🟢 **webdav.js runSync 拆阶段函数**：拉源/合并/写回/实体/上传/迁移清理六阶段拆独立小函数（`collectRemoteSources`/`mergeRemoteAll`/`writeBackLocal`/`cleanupMigrations`），`mergeSnapshots` 导出路径不变（17 单测不受影响）
- ✅ 验证：冒烟 34/34 · WebDAV 集成 19/19 · merge 17/17 · html 10/10 · P0 UI 场景 playwright 走查（搜索→编辑→清空搜索词全量恢复）全过
- 📌 说明：webdav.js 的 P1（墓碑下沉）与 P3（拆阶段）为同文件改动，合并为一个 commit（`be7c358`），便于整体回滚 webdav 域

### 环境清理 + 测试加固（2026-08-27 同日）

- 🔴 **数据清空事件排查结论**（2026-08-27 上午，两次「数据全空」）：最终判定为**环境问题**——机器上有 20+ 个不同版本/不同数据目录的遗留 `server.mjs` 实例（端口 8132/8133/8134/8135/8136/8137/8138/8139，08/26 起反复启动）长期未关，叠加冒烟测试默认连 8130 主服务污染 `users.json`（残留 u815113b/u5830b/u665694b 测试用户），多重混乱导致数据被清。**代码本身经全量测试 + 副本复现（富文本编辑/清除格式/删除标签/存入富文本）验证无可复现清空 bug**；数据经 WebDAV 远端快照完整恢复
- 🟢 **环境清理**：杀光全部遗留实例（仅保留 8130 主服务 + 8190 诊断页）；删除测试残留用户（u815113b/u5830b/u665694b）与孤儿测试文件（11111111）
- 🟢 **冒烟测试默认端口加固**（smoke-test.mjs）：默认端口 **8130 → 8131**，防止测试直连主服务污染真实数据（本次事件教训）；注释补血泪教训说明
- 🟢 **版本指纹**（server.mjs + package.json）：启动日志打印 `clipboard v0.6.14 (git commit)`——实例身份可追溯（多实例混跑事故的直接对策）；`package.json version` 0.5.1 → 0.6.14（此前与 CHANGELOG 脱节）
- 🟢 **平台版接入 tools-center**（delivery/platform/）：`tool.json`（id=clipboard / capabilities storage / dataFiles 声明）+ 部署说明——平台版 zip 解压到服务器 `tools/` 挂载目录即托管；数据落平台注入的 CAP_STORAGE_DIR（随平台 data/ 挂载持久化）
- 🟢 **发布规范 docs/发布规范.md**：专门发版规则——版本号（vX.Y.Z 语义化 + package.json 同步）/ 四形态产物命名 / 平台版 zip 结构约定 / 发版 Checklist（测试→打包→tag→gh release→服务器部署→验证）/ 回滚策略 / 历史记录

## v0.6.13 (2026-08-26)

### WebDAV 归档完整备份 + 归档可手动删除 + UI 微调

- 🟢 **会话去内存缓存（2026-08-27，users.js）**：文件 `sessions.json` 即唯一真相——createToken/verifyToken/destroyToken/pruneExpiredSessions 全部直接读写文件，删除 sessions Map 缓存与回滚逻辑。小体量（单机/低并发）下每请求读文件无感（实测读+解析 774B JSON 单次 ~14μs），换来模型最简：无"内存/文件两份数据"一致性问题（登录态丢/退出复活从根上消失）。⚠️ 注意事项：请求量达每秒几十次以上时再引入"内存缓存+TTL 1s"，勿提前加；loginGuard 限流表为临时状态保持内存
- 🟢 **富文本「清除格式」按钮**（app.js/index.html）：不需要富文本的条目，编辑弹窗（格式文本类型）新增「清除格式」按钮——确认后标记，保存时 `html` 置空转纯文本（后端 `sanitizeHtml` 空串即清空）；按钮变警告态「已标记清除 · 保存生效（点此取消）」，再点可取消；保存后卡片变普通文本（📝）、复制不再带格式。验证：富文本卡→清除→保存→html 清空 content 保留✅ / 再编辑类型变「文本」按钮不显示✅ / 冒烟 34/34

- 🟢 **WebDAV 快照纳入归档**（webdav.js）：快照 = 活跃区 ∪ 归档区（归档条目带 `archived` 标记）——此前归档只存本地不参与同步，现为完整备份，归档数据远端不丢
  - 拉回分拣写回：先 `saveArchive`（归档组替换）再 `saveClips`（活跃组，内部滚动自动追加归档）——顺序关键：先替换防滚动覆盖
  - 墓碑对归档删除同样传播（远端旧快照无归档标记时按活跃处理，滚动自动收敛）
- 🟢 **归档条目可删除**（clips.js `deleteArchivedClip` + 路由 + 前端）：归档卡片显示 ✕ 删除按钮，删除记墓碑（已配置 WebDAV 时）传播到远端，手动清理用不到的归档
- 🟢 **UI：主界面背景调深**（index.html）：`--elev #2F2F2F→#1F1F1F`、`--elev-hi #3A3A3A→#2A2A2A`，面板与背景更融合，整体观感贴近登录页深色沉浸
- 🟢 **UI：编辑弹窗标签区间距修复**（index.html）：`.edit-modal .tag-pick` 补 `margin-top:10px`（此前与上方别名/过期行零间距紧贴）+ 标签输入框改透明下划线式（与存入弹窗一致，去内阴影）
- 🟢 **UI：图片悬浮预览触发区收窄**（app.js）：open 从整卡 `mouseenter` 改为图片区域 `.imgwrap` `mouseenter`——此前鼠标悬停卡片按钮/标题/状态区也弹预览（用户反馈修正）；open 加防重（浮层挂卡内，移入浮层不再重建）；离开整卡才关闭（浮层不中途消失）
- 🟢 **编辑弹窗「归档」按钮**（clips.js `archiveClip` + 路由 `POST /api/clips/:id/archive` + 前端）：普通卡片编辑弹窗可一键移入归档区（确认提示「归档后可含归档查看、可随时恢复」）；归档参与 WebDAV 同步，用不到可删除
- 🟢 **归档可恢复**（clips.js `unarchiveClip` + 路由 `POST /api/clips/:id/restore` + 前端）：归档卡片右上角「↺ 恢复」移回活跃区，`updatedAt` 刷新为当前（防刚恢复被滚动立刻滚走）；编辑弹窗「归档」按钮改为与「取消」同尺寸同风格（88px 小按钮）
- 🔴 **WebDAV 文件实体上传失败修复**（webdav.js）：`ensureDir` 只建 `workbuddy/剪贴板/`，未建 `files/<uid>/` 实体目录——严格 WebDAV 服务器对「PUT 到不存在的目录」返回 409 → 同步报「文件实体上传失败:<文件名>」。现上传前先 MKCOL 确保 `files/` 与 `files/<uid>/` 存在；错误信息补 HTTP 状态码便于诊断
- 🟢 **一键无饱和度配色**（index.html/app.js）：顶栏 ◐ 按钮切换彩色/灰度配色——全部彩色令牌（金/砖红/绿/亮金/蓝/暖红 + 34 处硬编码 rgba）变量化（`--gold`/`--amber-rgb` 等 RGB 三元组），`html.mono` 一处覆盖全灰；localStorage 记忆，刷新保持
- 🟢 **修改用户名入口**（users.js `renameUser` + 路由 `POST /api/users/:id/name` + 数据管理左栏）：用户名输入框 + 保存，同名校验 409、长度校验，改名不销毁会话；改名后重建界面同步顶栏展示
- 🟢 **架构：mergeSnapshots 纯函数单元测试 + 主线总览文档**：合并裁决（墓碑/updatedAt 4 分支）补 `scripts/test-merge-snapshot.mjs`（17 项断言，node 直跑无需服务，测试即契约）；新增 `docs/main-flow.md` 主线 SSOT（一条剪贴板的生命周期一图流 + 各步入口函数 + 存储布局 + 支线索引），AGENTS 约定区挂引用
- 🟢 **健壮性：进程级异常兜底**（server.mjs）：`uncaughtException`/`unhandledRejection` 记录但不退出——路由层 try/catch 之外的最后防线（异步回调/定时器未预期错误不再导致进程崩溃、全用户掉线）
- 🟢 **并发：runSync per-user in-flight 锁**（webdav.js）：手动「一键同步」与定时 autoSync 同时触发时第二个抛 409「同步进行中」（此前并发会重复拉取/上传且 lastSyncAt 抖动）；autoSync 遇手动进行中跳过（不污染 lastSyncError）
- 🟢 **清理：死代码 3 处**（clips.js `getClip` / files.js `readFileBuffer` / app.js 废弃 `makeRichBtn` 防回滚占位）——全库确认无引用后删除；顺带修正 capabilities.md 过时行号引用
- 🟢 **双名模型重构（v0.6.13 账号名/显示名分离）**：`accountName` 账号名（创建后不可变，唯一身份键——WebDAV 寻址/跨设备识别）+ `displayName` 显示名（可随时改，仅影响展示）。业界标准（Steam/Google/WordPress 同款，Stack Overflow/Google AppEngine 官方文档确认）。**效果：改显示名不再影响 WebDAV 快照路径 → 删除 syncedName 改名迁移复杂度（runSync 回归单源合并）**；设备迁移 = 新部署创建相同账号名 → 同步即拉回。旧数据兼容：v0.6.13 前只有 name → accountName=name、displayName=name（读取归一不迁移文件）。前端：新建弹窗双输入 / 用户卡片显示名+账号名小字 / 数据管理账号名只读+显示名可改
- 🟢 **账号名修改（管理员级一次性操作）**：`POST /api/users/:id/account-name`——身份键修正通道（日常改名用显示名）；已配置 WebDAV 时记 `prevAccountName`，下次同步自动把旧账号名快照并入并迁移（幂等收敛）；前端数据管理账号名行「修改」按钮（确认弹窗提示迁移）。隔离验证 7/7（改账号名→同步→远端新名有数据+旧名删除+二次同步收敛）；真实执行：用户账号名「世界的风吹向你」→「billpotter」，11 条数据本地远端完全一致零丢失
- 🟢 **账号名仅限英文+数字（v0.6.13）**：`config.js ACCOUNT_NAME_RE`——账号名是身份键（WebDAV 文件名/跨设备识别），限 `[A-Za-z0-9]` 规避路径与编码兼容问题；显示名不受限（可中文）。createUser/changeAccountName 双入口校验 400「账号名仅限英文和数字」；前端新建/修改弹窗提示。存量中文账号名不强制迁移（仅约束新建/修改）。测试脚本中文用户名同步改英文（smoke 小明/小红→uRN、WebDAV测试→WebDAVTest）
- 🟢 **WebDAV 按账号名寻址（v0.6.13 设备迁移零配置）**：快照 `clipboard-<账号名>.json`、实体 `files/<账号名>/`——新部署创建相同账号名 → 配置 WebDAV → 同步即拉回全部数据（账号名即身份）。旧格式 `clipboard-<userId>.json` 首次同步自动并入并迁移（一次性自愈）。`runSync` 返回 `migrated` 标记
- 🔴 **原子写 Windows 兼容修复**（store.js `writeJson`）：Windows 上目标文件被瞬时锁定（Defender/索引扫描等外部进程）时 `renameSync` 抛 EPERM——此前直接抛错导致「写 sessions.json 失败」报错。现短重试 3 次（锁定通常 <200ms）+ 仍失败删除目标后重命名兜底（单实例数据可重建，接受极小窗口）；全失败清理残留 tmp 再抛 500（原文件未动不丢数据）。实测 300 次连续原子写零失败、无残留 tmp
- 🔴 **修改用户名持久化——治本重构**（users.js `getUserPublic` + 路由 `GET /api/users/me` + app.js）：改名保存后**强制刷新回落旧名**——根因是**设计缺陷**：`LS.cur` 缓存了 name/color（展示信息），boot 恢复登录直接 `state.current = saved` 用缓存对象——展示信息被当成持久化数据，缓存一旦过期就回落。**治本（消除缓存冗余源，而非补丁）**：①登录只存会话凭据 `{id, token}`（3 处统一，name/color 不再落缓存）②恢复登录 = 凭据恢复 + 必然从 `/api/users/me` 拉最新 name/color 填充（后端 users.json 是唯一权威源；旧缓存残留 name 仅作拉取失败兜底）③删掉"改名后补写 LS""boot 写回 LS"两处补丁——缓存里没有展示信息，永不"过期"。效果：改名/多端刷新永远一致，逻辑单源通顺
- 验证：归档同步端到端（505 条→5 归档→同步远端含 archived 标记→删归档→墓碑传播远端移除）全绿；图片预览三态验证（图片区触发✅/按钮区不触发✅/移入保持✅）；归档按钮端到端（按钮存在✅/活跃 8→7✅/含归档可见✅）；恢复闭环（按钮尺寸 88x41 一致✅/恢复 1→0✅/回活跃区✅）；文件实体同步（远端 files/<uid> 目录自动创建✅/实体上传成功✅）；配色切换（按钮✅/accent #C9A96E→#A8A8A8✅/刷新记忆✅/恢复彩色✅）；改名闭环（262→262x→262 顶栏同步✅）；**改名持久化治本版（登录 LS 纯凭据✅/刷新权威拉取✅/改名→刷新→保持 5/5✅）**；合并裁决单测 17/17 ✅；**同步并发锁（并发第二个 409✅/锁释放后成功✅）**；冒烟 34/34 零回归

## v0.6.12 (2026-08-26)

### 富文本复制链路修复批(最小单元链路诊断定位:Word 私有属性/body 属性保真 + 卡片分栏回归)

**排查方法**：新建最小单元链路诊断页（6 步：S1 捕获→S2 normalizeRichHtml→S3 存储→C2 buildWordDoc→C3 剪贴板写读→C4 还原渲染）+ 真实剪贴板写读 / 粘贴回读验证，逐段对比定位，不靠猜。

- 🔴 **S2 内联化丢 Word 私有属性修复**（`normalizeRichHtml`）：旧实现用 CSSOM `rule.style.cssText` 收集 `<style>` 块规则——CSSOM 只序列化浏览器认识的属性，`tab-interval` / `text-justify-trim` / `mso-*` 等 Word 私有属性全被丢弃、`word-wrap` 被规范化为 `overflow-wrap` → Word/WPS 粘贴还原不全（诊断页实测 cssText 只剩 2/6 属性）。改为 `style.textContent` 字符串级正则解析（跳过 `@` 规则），声明原样保留，元素匹配仍用 `el.matches`
- 🔴 **body 标签属性丢失修复**（`normalizeRichHtml`）：Word 文档级设置（`tab-interval:21.0pt;word-wrap:break-word;text-justify-trim:punctuation`）写在 `<body>` 标签上，旧实现返回 `doc.body.innerHTML`——body 自身属性不在 innerHTML 里，必然丢。现遍历 `body.attributes` 全部保留；`buildWordDoc` 兼容 `<body attrs>…</body>` 片段（属性并入外层 body，避免嵌套）
- 🟢 **body 文档级属性双保险**（`normalizeRichHtml`）：依据 CF_HTML 规范（MS Learn aa767917，粘贴应用主要解析 StartFragment/EndFragment 之间的 Fragment，body 属性在 Fragment 外的 context 里，部分应用不读），把 body 的 style 同时内联到段落元素（p/div/h1-h6/li），保证 Fragment 内也有文档级属性
- 🟢 **卡片富文本回归左右分栏 + 取消渲染预览**（app.js/index.html）：卡片内容区恢复左右分栏（左=普通文本 / 右=富文本，点击各复各的），顶部一行提示「T 普通文本 | ✦ 富文本」；删除富文本格式渲染预览（`richPreviewNodes`/`.rich-pv` 样式/分栏渲染分支）——预览渲染与真实 Word 差异大，取消；编辑弹窗富文本取消实时预览（单 textarea，保存后格式仍保留）；富文本复制按钮 🅡 转入备用（分栏即入口）
- 验证：最小单元诊断页 5 样例全链路无损 + 真实 Word 复制 html（42550 字）全链路——S2 lost[无]、C3a/C3b 无 CSS 属性丢失、真实 Ctrl+V 粘贴回读 4997 字（mso×92 / o:p×5 / xmlns / body 属性全保留）；开关组合实验确认当前输出 = Word 正确还原组合；冒烟 34/34 零回归

## v0.6.11 (2026-08-16)

### 细节审查修复批（代码审查 + 隔离实验验证，冒烟 34/34 · WebDAV 19/19 · 自动同步 3/3 · html 字段 10/10）

**排查方法**：全量代码审查（server.mjs + lib/core + lib/routes + public/app.js）+ 隔离脚本实测复现/验证，不靠猜。

- 🔴 **归档重复膨胀修复**（`rollToArchive`，clips.js）：活跃区超 500 条时，每次 `saveClips` 都把同一批最旧条目重复滚入归档——实测 800 条连续两次保存归档 300→600 翻倍（WebDAV 同步/高频操作触发）。现按 id 去重再追加，归档稳定不膨胀；新条目（updatedAt 新）仍正常留在活跃区
- 🔴 **`verifyPassword` 崩溃修复**（users.js）：passHash 损坏（手工编辑/半写坏文件，非合法 hex）时 `timingSafeEqual` 对不等长 Buffer 抛 `ERR_CRYPTO_TIMING_SAFE_EQUAL_LENGTH` → 登录 500。现先校验 hex + 长度一致，损坏数据登录安全返回 401
- 🟡 **导入非 UUID id 条目不可操作修复**（`sanitizeImported`，clips.js）：导入的条目 id 只截断不校验，非 UUID id 后续 `assertId` 全拒——编辑/复制/删除/置顶全 400（外部工具备份/手工 JSON 场景）。现校验 `ID_RE`，不合法则重新生成 UUID
- 🟡 **富文本编辑左右不一致修复**（前端 app.js）：富文本条目编辑纯文本后 `html` 字段不同步——卡片右栏预览与「复制带格式」拿到的还是旧内容。保存时 content 变了 → 按新文本重建 html（`textToHtml`）；JSON 预览「覆盖保存」同样处理
- 🟡 **登录限流表真修复**（`pruneLoginGuard`，users.js）：P1-3 原实现只清理「锁定过期且失败归零」的 key，**持续失败未达阈值（fails 1~7）且停手的 IP 永久残留**（假修复）。补 `lastFailAt` 时间戳，超过 `LOGIN_WINDOW_MS` 未再失败即回收
- 🟡 **WebDAV 实体同步扩展名兜底修复**（`extFor`，webdav.js）：无扩展名+未知 MIME 文件兜底由空串改 `.bin`（与 `saveFile` 的 bin 兜底一致）——此前远端路径 `<fileId>`、本地存 `<fileId>.bin`，恢复写盘无扩展名 → `getFilePath` 前缀匹配失败 → 下载 404
- 🟢 **`readBody` 超限流挂起修复**（helpers.js）：413 后 `req.resume()` 排空剩余流，防 keep-alive 复用挂起
- 🟢 **diag.html 并入静态缓存**（server.mjs）：此前每次请求 readFileSync，与 P1-4 缓存策略不一致
- 🟢 **WebDAV 自动同步间隔漂移修复**（前端 app.js）：间隔选项补 30 分钟（后端允许最小 30min，此前前端只有 1h 起，保存 30min 读回被 round 成 1h、再存变 60min）；读回/保存均精确到 0.5h
- 🟢 **下载 Blob URL 延迟释放**（前端 app.js）：`a.click()` 后立即 `revokeObjectURL` 部分浏览器下载中断，延迟 2s 释放
- 零行为回归（四套测试全绿）

## v0.6.9 (2026-08-16)

### 富文本复制链路重构定稿（数据流统一，清除历史缠绕）

**排查方法**：隔离诊断页 `public/diag.html`（`/diag.html` 路由）实测 iframe 内各 API 可用性 + 端到端模拟，不靠猜测。

**根因链（全链路）**：
1. 浏览器写剪贴板**强制剥 `<style>` 块/html/xmlns，只留 inline style**（Chromium 122+ `ClipboardWellFormedHtmlSanitizationWrite`，实测 `clipboard.write` 读回仅 98B）→ 样式必须内联
2. **Word 识别"来自 Word"靠 `xmlns:w="urn:schemas-microsoft-com:office:word"` 标记**（Microsoft roosterjs `isWordDesktopDocument.ts` / CKEditor paste-from-office）→ 片段不包装则 Word 判定"来自网页"，默认合并格式（宋体）
3. `execCommand + setData` 不受 Chromium 122+ sanitize 影响（W3C clipboard-apis#193），带 xmlns 的完整文档原样进 CF_HTML → Word 识别来源并保留格式（诊断实测通过）

**重构**（`public/app.js`，4 个干净函数，删除全部历史函数 `wrapWordDoc`/`inlineStyles`/`ensureWellFormedHtml`/`execCommandCopyRich`/`writeRichClipboard`）：
- **`normalizeRichHtml(html)`**（存入时统一）：DOMParser → `<style>` 块规则内联到元素 → 移除 style 块 → 干净内联片段存库
- **`buildWordDoc(html)`**（复制时统一）：片段 → 带 xmlns:o/w/m + StartFragment 的完整 Word 文档
- **`execCommandRich(rich, plain)`**：holder 纯文本承载选区（绝不 innerHTML 解析完整文档），setData 注入原始完整文档
- **`copyRich(html, text)`**：execCommand 主路径 + clipboard.write 兜底（都走 buildWordDoc）

**相关修复（v0.6.8 起累计）**：存入弹窗 paste 事件捕获 text/html、autoFill read() 单次调用、类型徽章「✦ 将存为：格式文本」自证、CSS 类名冲突 `.rich-pv`→`.re-pv`（消除卡片右栏污染）、编辑弹窗五类型、存入弹窗重写。
- 零行为回归（冒烟 34/34）

## v0.6.8 (2026-08-16)

### 富文本复制链路重写与实测闭环（场景走查 + 隔离诊断）

**根因终审**：Word 粘贴 html 时默认「合并格式」→ 显示无格式；选「保留源格式」即完整还原——复制/存入链路功能正常（隔离诊断页实测：paste 取 Word mso HTML 43408B、execCommand 复制读回含 inline style）。非代码缺陷。

- **copyRich 重写**（权威依据 MDN/web.dev/W3C#193）：`ensureWellFormedHtml` → Clipboard API 主路径 → execCommand 兜底（contenteditable+focus+setData 双格式），删除历史 wrap/顺序补丁
- **存入弹窗链路重写**：paste 事件捕获 `text/html`（手动粘贴可靠，无需 read 权限）；autoFill `read()` 只调一次（此前重复调用）；read 权限失败时提示手动粘贴保留格式
- **徽章自证**：存入弹窗类型徽章新增 `✦ 将存为：格式文本`（蓝色，pendingHtml 捕获成功可见）
- **CSS 类名冲突修复**：编辑弹窗富文本预览 `.rich-pv` → `.re-pv`（原全局类污染卡片右栏样式：虚线边框/角标/内边距）
- **隔离诊断页** `public/diag.html`（`/diag.html` 路由）：实测 iframe 内 Clipboard API/execCommand/paste 可用性，排障工具
- **场景走查报告** `docs/walkthrough/富文本复制链路走查.md`：R-1/R-2 实测降级 ⚪（环境支持全 API）
- 零行为回归（冒烟 34/34）

## v0.6.7 (2026-08-16)

### WebDAV 远端存储路径统一子目录（workbuddy/剪贴板/）

- **快照文件**：`<配置目录>/workbuddy/剪贴板/clipboard-<uid>.json`（原：配置目录根）
- **文件实体**：`<配置目录>/workbuddy/剪贴板/files/<uid>/<fileId><ext>`（勾选 syncFiles 时）
- `ensureDir` 改为**逐级 MKCOL**（根 → workbuddy/ → workbuddy/剪贴板/，WebDAV MKCOL 不支持递归）
- 连通测试探针文件同步移到子目录内
- 与 WebDAV 根的其他用途隔离，目录结构清晰；零行为回归（冒烟 34/34）

## v0.6.5 (2026-08-15)

### 卡片系统全量重构（方案 18 落地：三区骨架 + 类型专属内容）

- **统一骨架**：顶部徽章行（类型徽章 + 标题 + **状态徽章**）/ 弹性内容区 / 底部 meta 行（复制次数 + 标签 + 时间 + 操作按钮沉底常显）
- **状态徽章**（从操作区/meta 收敛到顶部行）：★ 置顶（金描边卡 + 金色徽章）/ ⏳ 过期（红）/ 归档（灰 + 整卡降透明）
- **类型专属内容区**（`makeCardBody`）：
  - 文本：2 行摘要（line-clamp）
  - JSON：**代码窗**（红/黄/绿三圆点装饰条 + 文件名 + 等宽缩进 + 键名金/字符串绿着色，安全重建不注入原始文本）
  - 链接：等宽 URL + 金色「↗ 打开链接」主按钮（原操作区「打开」移除）
  - 富文本：**左右对比双栏**——左=普通文本（纯文本样式）/ 右=富文本（白名单安全渲染格式：标题/粗体/列表金点/链接），点击各复各的
  - 图片：**cover 撑满**内容区（hover 放大镜浮层保留）
  - 文件：**类型图标卡**（PDF 红边 / ZIP 金边 / 其他中性，垂直居中）
- 归档卡不再显示「归档」操作区徽章（并入顶部状态徽章）
- **操作按钮对齐方案 18**：26px 方形图标钮（☆★ 置顶 / ✎ 编辑 / ✕ 删除 / ↓ 下载 / {} JSON），内凹底常显、hover 金、删除 hover 红；主操作统一金色胶囊（链接「↗ 打开链接」）
- **置顶实时生效**：点击后整体刷新——顶部 ★ 置顶徽章即时出现/消失 + 后端 pinned 优先排序生效（卡片跳到最前/归位）
- **操作按钮分组**：右上角只放 ✕ 删除；☆ 收藏（置顶）/ ✎ 编辑 / ↓ 下载 / {} JSON 在底部 meta 行右侧
- **图片复制计数修复**：`handleCardClick` 图片分支此前复制成功不计数（漏调 /copy 接口），且 `bumpCopyCount` 的 `c.type==="file"` 拦截会连带跳过图片——现图片复制同样 +1（普通文件下载仍不计数）
- **卡片高度策略**：恢复统一等高 `height:190px`（方案 18）；文本内容在等高内 flex 撑满 + **内部滚动**（`overflow-y:auto`，细滚动条 hover 卡片才显现）——长文本可滚动查看全文，不截断也不撑高卡片，混排零参差
- **富文本左右栏复制修复**：左右栏 onclick 中 `guard(...)` 漏末尾 `()`（guard 返回事件处理器但从未调用，点击只 stopPropagation、复制逻辑不执行）——补 `()` 后左=复制纯文本、右=复制带格式均生效；废弃的 `makeRichBtn` 同步修复
- **密码弹窗重设计（方案 22 极简聚焦）**：下划线式输入 + 浮动标签（focus/有内容时上移金色）+ 👁 显隐切换；头部「🔑 图标 + 标题 + 用户名胶囊」+ 底部「仅存哈希提示 + 关闭」；金色保存大按钮
- **数据管理弹窗重设计（方案 25 双栏工作台）**：左栏=设置与同步（缩放步长 + WebDAV 跨设备配置：url/user/pass + 实体同步/自动同步开关 + 保存配置/一键同步 + 状态区 P-104 失败可见）；右栏=备份与风险（导出/导入 + 红色危险区：全部清空/删除账号）；底部「数据仅存本地 JSON」+ 关闭；`renderWebdavSection` 适配 dm- 类双栏容器
- 新增 `hostOf` / `makeJsonPreview` / `makeFileIcon` / `makeLinkBody`；删除废弃的 `makeCardPreview` / `makeOpenBtn`
- CSS 全面重构（`.clip-card` 三区、`.status/.st.*`、`.pv/.code/.imgwrap/.filebody/.linkbody/.main-btn`、`.ops .b`），hover 金描边统一
- 零后端改动；冒烟 34/34 通过

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

### 部署工具（chore `36cd53b`）

- 新增 `scripts/deploy-via-api.mjs`：github.com 被墙时通过 GitHub Git Data API（api.github.com 可直连）推送本地仓库——blobs → tree → commit → ref（main 不存在则创建，已存在强推）

## v0.1.0 (2026-08-09)

### 初始化：多用户剪贴板工具（init `a3b7042`）

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
