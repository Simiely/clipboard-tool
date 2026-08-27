# EXE 轻量桌面版转型计划

> **目标**:把 Web 版(浏览器访问)转写为 Windows 轻量 EXE 桌面程序,力求 exe 最小、深色 UI、复用 WindowTinter 同款技术路线。
> **状态**:M2 骨架进行中(2026-08-27)。Web 版 v0.6.14 已发布,本计划已启动。

## 零、决策记录(2026-08-27,用户确认)

| 决策点 | 结论 |
|---|---|
| 工程位置 | **主仓库子目录 `clipboard-exe/`**——版本/发布/文档与主项目统一管理,release 资产加第三个 zip |
| 数据位置 | **exe 同目录 `data/`**——便携式,整个文件夹拷走即迁移(升级时注意保留 data/) |
| MVP 数据导入 | **包含**——Web 版导出 JSON 可直接导入 EXE(格式已对齐,成本低) |
| 环境 | .NET 9 SDK 9.0.300 + Desktop Runtime 9.0.5 已装(实测),M1 完成 |
| 框架版本 | **net9.0-windows**(STS)——官方深色 API SetColorMode 需要;net8 实测菜单无法系统级深色(见技术选型表) |

## 一、技术选型(基于 WindowTinter 实证)

| 项 | 选择 | 依据 |
|---|---|---|
| 语言/框架 | **C# WinForms,net9.0-windows**（STS） | **net9 提供官方深色 API `Application.SetColorMode(SystemColorMode.Dark)`**——菜单/滚动条/对话框等系统绘制部分整体深色（成熟方案非自绘，2026-08-27 实测菜单背景 253→47）；net8 无此 API（实测默认菜单在系统深色下仍浅色 253,253,253，SetPreferredAppMode 也无效）。net9 为 STS（2026-05 EOL），个人工具可接受，未来平滑升 .NET 10 LTS；用户机器已装 9.0.5 Desktop Runtime，零额外安装 |
| UI | 原生 WinForms 自绘深色 | 参考 WindowTinter 深色方案(沉浸式深色标题栏 + 自绘控件),对齐 Web 版黑金风格 |
| 剪贴板监听 | **Win32 `AddClipboardFormatListener`** | 原生 API,不依赖页面焦点——比浏览器 `clipboardchange` 强 |
| 托盘/开机自启 | WinForms NotifyIcon + 注册表/启动项 | WindowTinter 已验证的成熟做法 |
| 存储 | MVP 用 JSON(对齐 Web 版格式),V2 评估 SQLite | 数据格式与 Web 版兼容可互导 |

## 二、轻量化策略(核心诉求)

```
发布命令(参考 WindowTinter DEV_README 同款):
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

产出: 单个 Clipboard.exe,约 1~3 MB(框架依赖)
运行依赖: .NET 8 Desktop Runtime x64(用户自装,下载到桌面安装)
对比: 自包含发布(~70 MB exe)不在考虑范围——"最轻量化"要求框架依赖
```

**为什么框架依赖**:用户明确可安装运行环境 → exe 保持极小;runtime 装一次全机可用,后续版本 exe 更新即换即用。

## 三、运行环境(用户需安装,已确认可下载到桌面自装)

| 项 | 链接 | 说明 |
|---|---|---|
| .NET 9 Desktop Runtime x64(运行必需) | https://dotnet.microsoft.com/download/dotnet/9.0 | 下载 "Desktop Runtime 9.x x64" 安装（2026-08-27 实测本机已装 9.0.5） |
| .NET 9 SDK(仅开发需要) | 同上页面 "SDK 9.x" | 开发机装；只运行 exe 可不装 |

## 四、深色 UI 设计(对齐 Web 版黑金)

```
配色令牌(与 Web 版 index.html :root 对齐):
  --bg:#1A1A1A   窗口背景
  --elev:#1F1F1F 面板/卡片
  --gold:#C9A96E 主强调(金色)
  --text:#DADADA 正文
  --muted:#848484 次要
  --red:#E08A7A  危险
WinForms 实现:
  app.manifest 沉浸式深色标题栏(Win11)
  自绘 Card 控件(圆角 + 阴影,仿 Web 版新拟态)
  FlowLayoutPanel 卡片墙 / TextBox 搜索 / TagBar 标签 chips
```

## 五、功能范围(MVP → 迭代)

```
MVP(第一版,可用的核心):
  ☑ 单用户本地使用(数据存 %APPDATA%/Clipboard/ 或 exe 同目录 data/)
  ☑ 剪贴板监听:文本 / 链接 / 图片(自动捕获 + 手动存入)
  ☑ 卡片墙展示 + 点击复制回剪贴板
  ☑ 搜索(内容/标题)
  ☑ 深色 UI + 托盘常驻
V2:
  ☐ 标签体系 / 归档(含恢复/删除)
  ☐ 富文本(HTML 片段存储与复制)
  ☐ 置顶 / 复制次数排序
V3:
  ☐ WebDAV 同步(移植 mergeSnapshots 合并裁决 + 墓碑,按账号名寻址)
  ☐ 多用户(双名模型)

明确不做(MVP):多用户/WebDAV/富文本——先做单机核心体验,逐步迭代
```

## 六、里程碑

| 里程碑 | 内容 | 产出 |
|---|---|---|
| M1 环境准备 | 用户装 .NET 8 SDK + Runtime(桌面下载) | ✅ **已完成**(2026-08-27 实测 SDK 8.0.424 + Desktop Runtime 8.0.30) |
| M2 骨架 | WinForms 深色主窗体 + 托盘 + 剪贴板监听骨架 | 🚧 **进行中**——空壳 exe 可跑 |
| M3 MVP | 存储 + 卡片墙 + 搜索 + 复制 + 自动捕获 | 可用 exe(~2 MB) |
| M4 迭代 | 标签 / 归档 / 富文本 / 置顶排序 | v2 |
| M5 同步 | WebDAV 双向同步(复用合并裁决逻辑) | v3 |
| 发布 | 单文件发布 + GitHub Release(自动构建 tag 触发,参考 WindowTinter CI) | Release exe |

## 七、项目结构(规划)

```
clipboard-exe/                  (独立仓库或新目录)
  Program.cs                    入口 + 单实例 + 托盘
  MainForm.cs / MainForm.UI.cs  深色主窗体(参考 WindowTinter 拆分)
  ClipboardWatcher.cs           Win32 剪贴板监听
  Storage.cs                    JSON 存储(对齐 Web 版 clips.json 格式)
  CardControl.cs                自绘卡片控件
  AppSettings.cs                配置
  app.manifest                  深色标题栏 + DPI
  app.ico
```

## 八、风险与对策

| 风险 | 对策 |
|---|---|
| 剪贴板监听与 Office/输入法冲突 | 监听用低干扰模式,复制前短暂延迟 |
| 富文本格式保真(Word mso) | V2 阶段移植 normalizeRichHtml 字符串级逻辑;MVP 只存纯文本 |
| 数据迁移(Web → EXE) | MVP 数据格式对齐 Web 版 clips.json,提供导入入口 |
| 图片大文件占用 | 压缩存储 / 仅存引用 |

## 九、执行顺序(下一步)

1. 用户装 .NET 8 Desktop Runtime(下载到桌面安装)——**用户操作**
2. 确认 WindowTinter 构建环境(用户机器已有 .NET SDK 与否)
3. 新建 clipboard-exe 工程骨架(M2),跑通空壳 exe
4. MVP 迭代(M3)
