# 服务器版（Server Deployment）

> 状态：🚧 **规划中**。目标形态：公网/团队使用，跨设备 WebDAV 同步。

## 两种部署路径

### 路径 A：tools-center 平台托管（已有基础）

- `manifest.json` 已就绪（runtime: node + entry: server.mjs + port: 8130 + capabilities: ["storage"]）
- 平台注入 `CAP_STORAGE_DIR`（随平台备份/恢复），端口按 manifest 传入
- 平台自动构建 Docker 镜像（参考 tools-center 主仓库工作流）
- 优点：零运维、平台备份、健康检查（`/health`）已有
- 数据隔离：平台为每个部署分配独立存储目录，天然避免多实例混用

### 路径 B：独立服务器（裸机/VPS/Docker Compose）

```bash
# 方案：Docker 挂载数据卷
docker run -d -p 8130:8130 -v /data/clipboard:/data \
  -e CAP_STORAGE_DIR=/data \
  node:22 node /app/server.mjs 8130
```

- 前置：反向代理（Nginx/Caddy）+ HTTPS（可选）
- 多用户隔离：账号名唯一（409 校验），WebDAV 按账号名寻址
- 备份：数据卷快照 + 用户侧 WebDAV 双保险

## 服务器版要点

| 项 | 设计 |
|---|---|
| 安全 | 已有：scrypt 密码 / 登录限流 / UUID 白名单 / MIME 黑名单 / nosniff |
| 多租户 | 每用户独立数据文件，账号名唯一 |
| 同步 | WebDAV 双向合并 + 墓碑（用户自配，如坚果云/Nextcloud/ddnsto） |
| 高并发 | 当前文件直读写（~14μs/次）；⚠️ 每秒几十次以上需加"内存缓存+TTL 1s"（见 DEVELOPMENT.md） |
| 版本 | 启动日志打印 v版本 (commit)，部署实例可追溯 |

## 里程碑

- [ ] 确认部署路径（平台托管 vs 独立服务器）
- [ ] 独立服务器：Docker 镜像 + 数据卷 + 反向代理示例
- [ ] 压测：多用户并发读写验证（当前设计上限 ~每秒几十次请求）
