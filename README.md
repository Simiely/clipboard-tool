# 剪贴板 📋

多用户剪贴板管理工具（tools-center 平台工具，Node 零依赖）。暗黑新拟态（Neumorphism）界面，黑金奢华配色（酒红金 · v0.6.2+），首页极简墙布局（v0.6.3+），主页面双行工具栏（v0.6.4+），卡片系统全量重构（v0.6.5+：三区骨架 + 类型专属卡片 + 富文本左右对比分栏）。

## 功能

- **单一万能入口**：粘贴自动识别（URL→链接 / 其他→文本）、拖放/选择文件→文件条目、Ctrl+V 粘贴图片/文件；检测到复制内容自动弹出大窗口
- **一键复制 / 双击编辑**：文本/链接复制内容，图片点击直接复制到系统剪贴板（ClipboardItem），其他文件点击下载
- **富文本双格式复制（v0.6.0）**：从网页/Word 复制带格式内容 → 存入时自动检测 `text/html`，卡片显示 **🅡 富文本按钮**——点它粘贴到 Word/飞书保留格式，点卡片复制纯文本
- **智能排序**：星标置顶 → 使用次数优先 → 标签相近归拢 → 内容相似归拢（10 字符片段倒排索引，毫秒级）
- **拼音首字母搜索**：内置 3755 常用字映射表，`sf` 可搜到"身份"（注：拼音匹配仅前端搜索框输入生效；后端 `?q=` 查询为子串匹配）
- **标签体系**：点选已有标签 + 输入新建，列表标签过滤
- **多用户**：无密码零摩擦进入 / 可选密码锁，会话持久化（重启不掉线，30 天有效）
- **WebDAV 备份同步**：单向全量备份 + 双向合并同步（墓碑机制防删除复活、全部清空不传播删除、定时自动同步默认 12h、可选同步文件实体）
- **滚动归档**：活跃区上限 500 条/用户，超出自动移入归档（零丢失），「含归档」可搜历史（注意：归档只存本地，不参与 WebDAV 同步）
- **本地备份**：一键导出全部（含归档）JSON / 导入合并（同 id 取新），不依赖 WebDAV
- **URL 自动清理**：保存链接自动剔除 UTM 等追踪参数
- **标签管理**：设置里重命名 / 删除标签（跨全部条目含归档同步生效）
- **JSON 格式化预览**：文本条目是 JSON 时一键美化查看 / 复制 / 覆盖保存
- **安全**：UUID 白名单防路径穿越、原子写、登录限流、上传黑名单（拒可执行/脚本类）、下载强制 attachment

## 技术栈

- 后端：Node.js（内置 `http` + `fetch`，零第三方依赖）
- 前端：原生 HTML/JS（Alpine 式轻量、无框架），暗黑新拟态双阴影浮雕风格（设计令牌集中在 `:root`）
- 存储：JSON 文件（原子写），WebDAV 用 HTTP 协议直连

## 运行

```bash
node server.mjs [port]        # 默认 8130
# 数据目录默认 ./.data（可用 CAP_STORAGE_DIR 覆盖）
```

## 测试

```bash
# 冒烟测试（需先启动独立实例，勿对真实数据目录跑）
CAP_STORAGE_DIR=/tmp/clip-test PORT=8131 node server.mjs 8131
TEST_PORT=8131 node scripts/smoke-test.mjs

# WebDAV 端到端（需再起 mock WebDAV）
node scripts/mock-webdav.mjs 8180 /tmp/mock-webdav
node scripts/test-webdav-sync.mjs
node scripts/test-auto-sync.mjs

# 富文本 html 字段单测（v0.6.0，无需起服务，独立数据目录）
node scripts/test-html-field.mjs
```

> Windows 提示：`/tmp/...` 是类 Unix 路径写法；Windows 下请用绝对路径
> （如 `C:/Temp/clip-test`、`C:/Temp/mock-webdav`），数据目录同样传给
> `TEST_DATA_DIR` 保持一致（`TEST_DATA_DIR=C:/Temp/clipboard-test node scripts/test-webdav-sync.mjs`）。

## 目录结构

```
server.mjs          # 入口：HTTP 装配 + 路由 + 静态服务 + 过期清扫 + 自动同步定时器
manifest.json       # tools-center 平台声明
package.json        # 工程元数据：type:module + Node>=22.7 + npm scripts（start/smoke/test:webdav/test:auto-sync/test）
lib/core/           # 纯业务逻辑（store/clips/users/files/webdav）
lib/routes/         # 路由薄层 + 会话中间件
public/index.html   # 单文件前端（暗黑新拟态，设计令牌在 :root）
scripts/            # 测试与工具脚本（mock WebDAV、冒烟、集成、html 字段单测、复杂度测量）
```

## 文档

- [开发文档 DEVELOPMENT.md](DEVELOPMENT.md) — 架构说明与关键问题记录
- [变更日志 CHANGELOG.md](CHANGELOG.md) — 版本记录
- [AI 项目规则 AGENTS.md](AGENTS.md) — 技术栈 / 关键坑 / 约定
