# Clipboard EXE 桌面版

> 状态：✅ **MVP + M5 迭代完成**（2026-08-28：数据层 / 捕获 / 卡片墙 / 搜索 / 标签 / 归档 / 编辑 / 富文本 / URL 清理）。规划与决策详见 [`../docs/exe-plan.md`](../docs/exe-plan.md)。

Windows 轻量桌面剪贴板工具（C# WinForms, net9.0-windows）。托盘常驻、原生剪贴板监听、黑金深色 UI（含菜单/滚动条/对话框——官方 `Application.SetColorMode(SystemColorMode.Dark)`，非自绘）。

## 功能

- **自动捕获（仅前台激活时）**：复制文本 / 链接（URL 识别）/ 图片 → 自动存入卡片墙；**窗口处于前台激活状态时才捕获**，最小化到托盘 / 失活时停止读取剪贴板（隐私优先，用户确认 2026-08-28）
- **URL 自动清理**：链接捕获/编辑时自动剔除 UTM/fbclid/gclid/igshid/from/spm 等 24 个追踪参数（对齐 Web 版 cleanUrl）
- **富文本**：复制带格式内容（网页/Word）→ 自动捕获纯文本 + 保留 HTML 片段；右键「复制富文本」原文回写保留格式
- **去重**：与最近一条相同的内容重复复制 → 不产生重复条目，仅刷新时间置顶
- **卡片墙**：黑金深色卡片（类型徽标 T/L/I + 标题 + 内容摘要 + 相对时间 + 置顶★/复制次数），悬停金边高亮；**双击编辑**
- **点击复制**：文本/链接复制回剪贴板；图片还原为 PNG 复制；复制次数 +1 参与排序
- **搜索**：顶部搜索框实时过滤（标题/内容/链接包含，忽略大小写；Esc 清空）
- **标签体系**：顶栏标签 chips（「全部」+ 各标签，点击过滤）；编辑弹窗内标签 chips（点选已有 + 输入即时新增）；重命名/删除标签跨活跃+归档全部条目同步生效
- **归档**：编辑弹窗/右键菜单「移入归档 / 从归档恢复」；工具栏「含归档」开关查看归档条目
- **右键菜单**：复制 / 复制富文本（有 html 时）/ 编辑 / 置顶 / 归档（或恢复）/ 删除（含图片实体清理）
- **导入导出**：导出 = Web 版格式 JSON（`{app,version,exportedAt,clips[]}`），导入 = Web 版导出文件直接合并（同 id 取 updatedAt 新者，非 UUID id 自动重生成）
- **托盘常驻**：点 X / 最小化 → 托盘；双击恢复；托盘菜单「退出」才真正退出；单实例
- **手动存入**：工具栏「存入」立即保存当前剪贴板（不受前台捕获开关限制）

## 数据与兼容

- 数据目录：**exe 同目录 `data/`**（便携式，整个文件夹拷走即迁移）
  - `data/clips.json`：条目（16 字段与 Web 版 `publicClip` 完全对齐，camelCase）
  - `data/files/`：图片实体（PNG，`fileId` 引用）
  - `data/clipboard-exe.log`：运行日志（首行版本指纹）
- Web 版「数据管理 → 导出全部」的 JSON 可直接「导入」到 EXE，反之亦然

## 目录

```
clipboard-exe/
  ClipboardExe.csproj   工程（net9.0-windows, 单文件框架依赖发布）
  Program.cs            入口：单实例互斥 + 托盘 + 官方深色模式 + 版本指纹
  MainForm.cs           主窗体逻辑：卡片墙渲染 / 过滤 / 复制 / 编辑 / 右键菜单 / 导入导出
  MainForm.UI.cs        布局部分（工具栏 / 标签栏 / 状态栏 / 卡片墙容器 / 空态提示）
  ClipboardWatcher.cs   Win32 剪贴板监听封装 + 捕获/去重/类型识别/富文本
  CleanUrl.cs           URL 追踪参数清理（UTM 等 24 个，移植 Web 版）
  Storage.cs            JSON 数据层：原子写 / 排序 / 标签 / 归档 / Web 导入导出合并
  ClipItem.cs           条目模型（与 Web 版 publicClip 字段对齐）
  CardControl.cs        自绘卡片控件（黑金风格，悬停金边）
  EditDialog.cs         编辑弹窗（标题/内容/链接/标签 chips/归档-恢复）
  NativeMethods.cs      P/Invoke 集合
  IconFactory.cs        程序化图标（黑底金剪贴板）
  app.manifest          沉浸式深色标题栏 + PerMonitorV2 DPI
```

## 构建与发布

```cmd
:: 开发构建（Debug）
dotnet build clipboard-exe

:: 单文件发布（约 200 KB，需用户装 .NET 9 Desktop Runtime）
dotnet publish clipboard-exe -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
:: 产出: clipboard-exe/bin/Release/net9.0-windows/win-x64/publish/Clipboard.exe
```

## 运行要求

- **.NET 9 Desktop Runtime x64**（https://dotnet.microsoft.com/download/dotnet/9.0）
- Windows 10/11

## 测试记录（2026-08-28）

- Storage 单测 19/19（原子写 / 排序 / Web 导出格式 / 导入合并去重 / 删除+文件清理 / 损坏容错 / CleanUrl 8 项 / 标签重命名删除跨条目 / 归档恢复）
- 端到端（Debug 实例）：自动捕获文本 ✅、URL→link ✅、重复复制去重 ✅、数据落盘格式 ✅
- 图片捕获 / 标签 / 归档 / 编辑 / 富文本 GUI 交互：代码与单测就绪，需真机实测（沙箱无法模拟剪贴板图片与真实焦点切换）

## 里程碑

- [x] M1 环境（.NET 9 SDK + Desktop Runtime，2026-08-27 实测就绪）
- [x] M2 骨架（空壳 exe 可跑 + 深色菜单 SetColorMode 实测 253→47）
- [x] M3 数据层：JSON 存储对齐 Web 版字段 + Web 导出 JSON 导入（2026-08-28）
- [x] M4 MVP：剪贴板捕获（文本/链接/图片）+ 卡片墙 + 点击复制 + 搜索（2026-08-28）
- [x] M5 迭代：标签体系 / 归档 / 编辑弹窗 / 富文本 / URL 清理（2026-08-28）
- [ ] M6 WebDAV 双向同步（移植 mergeSnapshots + 墓碑语义，V3）

## 与 Web 版的关系

- 数据格式一致（16 字段同构）→ Web 版导出 JSON 可直接导入
- 单机场景用 EXE，多用户/局域网用本地服务版（8130）
