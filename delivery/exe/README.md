# EXE 桌面版（Desktop EXE）

> 状态：🚧 **规划中**（2026-08-26 立项）。详细技术方案见 [`../docs/exe-plan.md`](../docs/exe-plan.md)。

## 一句话定位

Windows 轻量桌面程序，替代"浏览器访问本地服务"——托盘常驻、原生剪贴板监听（不依赖页面焦点）、深色黑金 UI。

## 技术要点（详见 exe-plan.md）

| 项 | 选择 |
|---|---|
| 语言/框架 | C# WinForms, net8.0-windows（LTS） |
| 发布 | 框架依赖单文件，约 1~3 MB（用户自装 .NET 8 Desktop Runtime） |
| 剪贴板监听 | Win32 `AddClipboardFormatListener`（比浏览器 clipboardchange 强） |
| 存储 | MVP 用 JSON（与 Web 版格式兼容可互导） |
| 进度 | 待用户安装 .NET 8 后启动 M2 骨架 |

## 里程碑

- [ ] M1 环境：安装 .NET 8 SDK / Desktop Runtime（用户操作）
- [ ] M2 骨架：WinForms 主窗口 + 深色主题 + 托盘
- [ ] M3 数据层：JSON 存储，与 Web 版格式对齐
- [ ] M4 剪贴板监听 + 存入/列表/复制
- [ ] M5 WebDAV 同步（复用墓碑语义）

## 与本地服务版的关系

- 数据格式一致 → 可互相导入（导出/导入 JSON）
- EXE 版完成后可选：本地服务版继续用于多用户/局域网场景，EXE 用于单机场景
