# AGENTS.md - FantasyTown.Auth

## Project Overview

Minecraft 皮肤站微内核（Yggdrasil 外置登录 + Web 账户注册），基于 .NET 10 / ASP.NET Core 10。

## Critical Constraints

- **设计文档冻结**：`.documents/NET10软件工程设计方案.md` (V5) 是唯一实现依据，改动须走评审
- **防错清单**：实现任何机制前必查 `.documents/NET10实施指导.md` 第 4 章
- **模块纪律**：模块间只经 `Shared/` 接口通信，禁止跨模块引用 DbSet
- **Git 规范**：使用约定式提交（conventional commits），颗粒度精确到功能块
- **不要 push**：未经允许不得 git push
- **不要改规划**：未经允许不得更改设计文档中的冻结项

## Architecture

垂直切片模块化单体，单部署单元：

```
Modules/
├── Accounts/          # Web + Admin + Application + Domain + Infrastructure
├── Yggdrasil/         # Authserver + Sessions + Profiles + Protocol
└── Shared/            # Result, IClock, IClientIp, IAuditChannel, SiteOptions
Persistence/           # AuthDbContext, EF Migrations
```

**关键约束**：Accounts 通过 `IPlayerDirectory`（Shared 接口）暴露给 Yggdrasil，禁止直接访问 DbSet。

## External Dependencies

| 服务 | 启动命令 |
|---|---|
| MySQL 8.0+ | `docker run -d -p 3306:3306 -e MYSQL_ROOT_PASSWORD=dev -e MYSQL_DATABASE=fantasytown_auth mysql:8` |
| Redis 7+ | `docker run -d -p 6379:6379 redis:7` |

连接串：`appsettings.Development.json` 或环境变量。

## Build & Test Commands

```bash
# 还原依赖
dotnet restore

# 构建
dotnet build

# 运行单元测试（无需外部服务）
dotnet test tests/FantasyTown.Auth.UnitTests

# 运行集成测试（需要 MySQL + Redis 容器）
dotnet test tests/FantasyTown.Auth.IntegrationTests

# 运行契约测试
dotnet test tests/FantasyTown.Auth.ContractTests

# EF 迁移
dotnet ef migrations add <MigrationName> --project src/FantasyTown.Auth
dotnet ef database update --project src/FantasyTown.Auth
```

## Testing Conventions

- **单元测试**：纯函数（PermissionRules, IsBanEffective, ManagementGuard），无容器依赖
- **集成测试**：Testcontainers（Redis + MySQL），测试并发不变量
- **契约测试**：Yggdrasil 协议 golden fixtures，逐字节比对

## Key Patterns

- **Program.cs 装配顺序**：错误顺序 = 静默失效，严格按文档 §2.3
- **Cookie**：`__Host-ft_auth`（__Host- 前缀 + Secure + HttpOnly + SameSite=Lax）
- **权限判定**：白名单精确匹配（`is moderator => role is X or Y`），禁止 `>=`
- **封禁判定**：`IsBanEffective(flag, until, nowUtc)` 时间谓词，bool 仅快速短路
- **PERM 快照**：回源空结果 = 封禁等效哨兵（非 null、非 IsBanned=false）

## Frozen Items (Do Not Modify Without Review)

1. DB Schema：六表结构 + UNIQUE 约束 + auth_log 双索引
2. Redis 键空间：13 类键名、TTL、语义
3. 权限判定语义：四级枚举、白名单判定、Guard 三函数
4. 管理管线顺序：Antiforgery → 策略 → 越权矩阵 → 二次确认 → 领域命令
5. 协议 DTO：Yggdrasil 八端点响应字段与错误格式
6. 封禁状态机：IsBanEffective、条件更新闸门、每玩家至多一条 active

## Milestones

| 阶段 | 产出 | 验收门 |
|---|---|---|
| P0 | EF/Redis/Page/site/可观测性 + fixture 框架 | /health 绿 |
| P1 | Accounts 注册/登录/找回/重置 + 安全修复 | 并发注册恰 1；锁定/封禁时序绿 |
| P2 | Yggdrasil Authserver | golden 逐字节一致 |
| P3 | Sessionserver join/hasJoined/profile | hasJoined P99<50ms |
| P4 | 管理域 /admin 页面 + ManagementGuard | 越权矩阵全覆盖 |
| P5 | 加固压测 | validate 5k RPS 稳态 |
