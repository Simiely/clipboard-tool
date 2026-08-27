# Clipboard EXE 桌面版

> 状态：🚧 **M2 骨架**（2026-08-27 启动）。规划与决策详见 [`../docs/exe-plan.md`](../docs/exe-plan.md)。

Windows 轻量桌面剪贴板工具（C# WinForms, net8.0-windows）。托盘常驻、原生剪贴板监听（不依赖焦点）、黑金深色 UI。

## 目录

```
clipboard-exe/
  ClipboardExe.csproj   工程（net8.0-windows, 单文件框架依赖发布）
  Program.cs            入口：单实例互斥 + 托盘 + 启动主窗体（版本指纹）
  MainForm.cs           深色主窗体 + 剪贴板监听宿主 + 数据目录 + 托盘交互
  ClipboardWatcher.cs   Win32 AddClipboardFormatListener 封装
  NativeMethods.cs      P/Invoke 集合（剪贴板/深色标题栏/单实例唤醒）
  IconFactory.cs        程序化图标（黑底金剪贴板）
  app.manifest          沉浸式深色标题栏 + PerMonitorV2 DPI
```

## 构建与发布

```cmd
:: 开发构建（Debug）
dotnet build clipboard-exe

:: 单文件发布（约 1~3 MB，需用户装 .NET 8 Desktop Runtime）
dotnet publish clipboard-exe -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
:: 产出: clipboard-exe/bin/Release/net8.0-windows/win-x64/publish/Clipboard.exe
```

## 运行

- 双击 `Clipboard.exe` 即可；首次启动在 **exe 同目录 `data/`** 生成数据与日志
- 托盘常驻：点 X / 最小化 → 托盘；托盘菜单「退出」才真正退出
- 单实例：重复启动会唤醒已有实例主窗体
- 版本指纹：`data/clipboard-exe.log` 首行记录 `v版本 (commit)`

## 里程碑

- [x] M1 环境（.NET 8 SDK + Desktop Runtime，2026-08-27 实测就绪）
- [x] M2 骨架（本目录）——空壳 exe 可跑
- [ ] M3 数据层：JSON 存储对齐 Web 版字段 + Web 导出 JSON 导入
- [ ] M4 MVP：剪贴板捕获（文本/链接/图片）+ 卡片墙 + 点击复制 + 搜索
- [ ] M5 迭代：标签 / 归档 / 富文本 / 置顶排序（V2）
- [ ] M6 WebDAV 双向同步（移植 mergeSnapshots + 墓碑语义，V3）

## 与 Web 版的关系

- 数据格式一致（`clips.json` 同字段）→ Web 版导出 JSON 可直接导入
- 单机场景用 EXE，多用户/局域网用本地服务版（8130）
