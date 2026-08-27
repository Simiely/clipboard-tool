# 交付形态总览（Delivery Plan）

> 本目录标记 clipboard-tool 的**三种交付形态**，各形态独立子目录，避免"本地/服务器/EXE"混用混淆。
> 2026-08-27 数据清空事故教训：多个不同版本实例混跑导致数据被清。**每次拉起的实例必须能自我标识版本**（server.mjs 启动日志打印 `v版本 (commit)`）。

## 四形态对比

| 形态 | 子目录 | 状态 | 运行方式 | 数据目录 | 适合场景 |
|---|---|---|---|---|---|
| **本地服务版**（Web） | `local-service/` | ✅ 在产 v0.6.14 | `node server.mjs 8130` | `./.data`（CAP_STORAGE_DIR 可覆盖） | 个人/局域网多用户，浏览器访问 |
| **平台版**（tools-center） | `platform/` | ✅ 可用 v0.6.14 | 平台托管（tools/ 挂载 + tool.json 声明） | 平台注入 CAP_STORAGE_DIR | 服务器集中部署，多工具平台统一管理 |
| **EXE 桌面版** | `exe/` | 🚧 规划中 | 双击 `Clipboard.exe`（.NET 8） | 本地 JSON（与 Web 版格式兼容可互导） | 单机桌面，托盘常驻，剪贴板原生监听 |
| **服务器版** | `server/` | 🚧 规划中 | `start-server.cmd`（独立部署） | 默认 ./.data 或参数指定 | 公网/团队，独立部署 |

## 通用约定（四形态一致）

- **数据格式**：JSON（users.json / sessions.json / users/<uid>.json + archive/tombstones/webdav + files/），各形态互导兼容
- **WebDAV 备份**：快照 `clipboard-<accountName>.json` + 实体 `files/<accountName>/`，按账号名寻址，设备迁移=建同名账号→同步
- **版本标记**：`package.json version` + git commit（server.mjs 启动日志打印），实例身份可追溯
- **端口约定**：主服务 **8130**（本地服务版）、测试实例 **8131+**（独立数据目录，禁止连 8130）
- **测试铁律**：所有测试必须指向独立数据目录实例（`smoke-test.mjs` 默认 8131）
- **发版规则**：见 `../docs/发布规范.md`（版本号/产物命名/打包/发布/部署全流程）

## 详细计划

- 本地服务版：见 `local-service/README.md` + `start.cmd`
- 平台版：见 `platform/README.md` + `tool.json`
- EXE 版：详细技术方案见 `../docs/exe-plan.md`（本目录 `exe/README.md` 为状态入口）
- 服务器版：见 `server/README.md`
