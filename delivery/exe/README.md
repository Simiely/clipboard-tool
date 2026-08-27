# EXE 桌面版（Desktop EXE）

> 状态：🚧 **M2 骨架进行中**（2026-08-27 启动）。决策已定：工程在**主仓库子目录 `clipboard-exe/`**、数据存 **exe 同目录 `data/`**、MVP 含 **Web 版 JSON 导入**。详细技术方案见 [`../docs/exe-plan.md`](../docs/exe-plan.md)。

## 一句话定位

Windows 轻量桌面程序，替代"浏览器访问本地服务"——托盘常驻、原生剪贴板监听（不依赖页面焦点）、深色黑金 UI。

## 技术要点（详见 exe-plan.md）

| 项 | 选择 |
|---|---|
| 语言/框架 | C# WinForms, net9.0-windows（官方深色 API SetColorMode；.NET 8 无此 API，菜单无法系统级深色） |
| 发布 | 框架依赖单文件，约 170 KB（用户自装 .NET 9 Desktop Runtime） |
| 深色菜单/滚动条/对话框 | `Application.SetColorMode(SystemColorMode.Dark)`（官方 API，非自绘） |
| 剪贴板监听 | Win32 `AddClipboardFormatListener`（比浏览器 clipboardchange 强） |
| 存储 | MVP 用 JSON（与 Web 版格式兼容可互导） |
| 进度 | M2 骨架完成（深色菜单已修） |

## 里程碑

- [x] M1 环境：安装 .NET 8 SDK / Desktop Runtime（✅ 2026-08-27 实测就绪）
- [ ] M2 骨架：WinForms 主窗口 + 深色主题 + 托盘 + 剪贴板监听骨架
- [ ] M3 数据层：JSON 存储，与 Web 版格式对齐 + 导入入口
- [ ] M4 剪贴板监听 + 存入/列表/复制
- [ ] M5 WebDAV 同步（复用墓碑语义）

## 与本地服务版的关系

- 数据格式一致 → 可互相导入（导出/导入 JSON）
- EXE 版完成后可选：本地服务版继续用于多用户/局域网场景，EXE 用于单机场景
