# Clipboard EXE 桌面版

> 状态：✅ **v0.8.0 重写版**（2026-08-28 交互层清零重写，严格对齐 Web 版操作逻辑）。规划与决策详见 [`../docs/exe-plan.md`](../docs/exe-plan.md)。

Windows 轻量桌面剪贴板工具（C# WinForms, net9.0-windows）。托盘常驻、原生剪贴板监听、黑金深色 UI（官方 `Application.SetColorMode(SystemColorMode.Dark)`）。

**重写原则**：不再"自创交互"，逐项对照 Web 版 app.js 迁移——确认式存入弹窗 / 类型化卡片 / 富文本分栏 / 卡片按钮行 / 批量编辑 / 拼音搜索 / JSON 预览，数据格式与 Web 版互导。

## 功能（对齐 Web 版操作逻辑）

- **确认式存入**（对齐 `openPasteModal`）：前台激活时复制文本/链接/图片 → **自动弹出存入确认窗**（类型徽章 + 内容可编辑 + 标题 + 标签 chips + 富文本提示）→「存入」落库 /「放弃」丢弃；手动「存入」按钮或**空格键**同样打开
- **去重**：与最近一条相同的内容重复复制 → 不弹窗（静默刷新置顶，不产生重复）
- **类型化卡片**（对齐 `clipCard`）：文本卡（摘要 + JSON `{}` 按钮 + 富文本分栏）、链接卡（host 徽章 + URL）、图片卡（缩略图）；整卡点击=复制
- **富文本分栏**（对齐 `makeRichSplit`）：卡片内容区「T 普通文本 | ✦ 富文本」两段，点击分别复制纯文本/富文本（Word/网页格式保留）
- **卡片按钮行**（对齐 Web 卡片按钮）：★置顶 / ✎编辑 / ↺归档恢复 / ✕删除，直接显示在卡片上
- **URL 自动清理**：捕获/编辑链接自动剔除 UTM 等 24 个追踪参数（对齐 `cleanUrl`）
- **搜索**：标题/内容/URL 子串 + **拼音首字母**（`sf`→身份，3755 常用字映射表原样搬自 Web）
- **过滤**：标签 chips（全部+各标签）+ 类型过滤（全部/文本/链接/图片文件）+ 含归档开关
- **编辑弹窗**（对齐 `openEditModal`）：标题/内容/链接/标签 chips/清除格式（富文本转纯文本）/归档/删除
- **JSON 格式化预览**（对齐 `openJsonPreview`）：文本条目是 JSON 时卡片出 `{}` 按钮——美化/复制/覆盖保存
- **批量编辑**（对齐 `setBatchMode`）：工具行「编辑」进入多选 → 卡片勾选 → 底部批量条（全选当前页/批量加标签/批量减标签/批量删除/完成）
- **导入导出**：Web 版格式 JSON 互导（`{app,version,exportedAt,clips[]}`，同 id 取新、非 UUID 重生成）
- **托盘常驻 / 单实例**：点 X/最小化 → 托盘（停止捕获）；托盘「退出」才真正退出
- **前台捕获开关**：窗口激活才自动捕获，失活/最小化停止读剪贴板（隐私优先，用户确认）

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
  Program.cs            入口：单实例互斥 + 托盘 + 官方深色模式 + 版本指纹 + 日志
  MainForm.cs           主窗体逻辑：过滤渲染 / 存入弹窗联动 / 复制 / 批量编辑 / 导入导出
  MainForm.UI.cs        布局：工具栏 / 类型过滤 / 标签栏 / 卡片墙 / 批量条 / 状态栏
  ClipboardWatcher.cs   剪贴板监听 + 确认式捕获（类型识别/去重/富文本/前台开关）
  CleanUrl.cs           URL 追踪参数清理（UTM 等 24 个，移植 Web 版）
  Storage.cs            JSON 数据层：原子写 / 排序 / 标签 / 归档 / 导入导出 / 清空
  ClipItem.cs           条目模型（与 Web 版 publicClip 字段对齐）
  Pinyin.cs             拼音首字母搜索（3755 字映射，原样搬自 Web PY_GROUPS）
  CardControl.cs        类型化卡片（徽章/摘要/分栏/按钮行，对齐 Web clipCard）
  CaptureDialog.cs      存入确认弹窗（对齐 Web openPasteModal）
  EditDialog.cs         编辑弹窗（清除格式/归档/删除，对齐 Web openEditModal）
  JsonPreviewDialog.cs  JSON 格式化预览（对齐 Web openJsonPreview）
  InputDialog.cs        单行输入弹窗（批量标签用）
  NativeMethods.cs      P/Invoke 集合
  IconFactory.cs        程序化图标（黑底金剪贴板）
  app.manifest          沉浸式深色标题栏 + PerMonitorV2 DPI
```

## 构建与发布

```cmd
dotnet build clipboard-exe
:: 单文件发布（约 240 KB，需用户装 .NET 9 Desktop Runtime）
dotnet publish clipboard-exe -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
:: 产出: clipboard-exe/bin/Release/net9.0-windows/win-x64/publish/Clipboard.exe
```

## 运行要求

- **.NET 9 Desktop Runtime x64**（https://dotnet.microsoft.com/download/dotnet/9.0）
- Windows 10/11

## 测试记录（2026-08-28 v0.8.0）

- 单测 18/18：拼音首字母 8 项（sf→身份、jtb→剪贴板、大小写、不命中）/ CleanUrl 3 项 / Storage 回归 7 项（排序/标签/归档/导入导出/清空）
- 构建 0 警告 0 错误；发布单文件约 240 KB
- 待真机验证：确认式存入弹窗 / 卡片分栏复制 / 图片捕获 / 批量编辑 / 前台捕获开关 / 拼音搜索

## 里程碑

- [x] M1~M5 环境 / 骨架 / 数据层 / MVP（v0.7.x，已由 v0.8.0 重写取代）
- [x] **v0.8.0 交互重写**：交互层清零，逐项对齐 Web 版操作逻辑（2026-08-28）
- [ ] M6 WebDAV 双向同步（移植 mergeSnapshots + 墓碑语义，V3）
- [ ] 待补：图片 hover 悬浮预览（对齐 Web bindImageHoverPreview）

## 与 Web 版的关系

- 数据格式一致（16 字段同构）→ Web 版导出 JSON 可直接导入
- 单机场景用 EXE，多用户/局域网用本地服务版（8130）
