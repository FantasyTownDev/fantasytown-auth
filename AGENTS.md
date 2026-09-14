# AGENTS.md - FantasyTown.Auth

## Project Overview

Minecraft 皮肤站微内核（Yggdrasil 外置登录 + Web 账户注册），基于 .NET 10 / ASP.NET Core 10。

## Target Platforms

| 系统 | 架构 | 支持 |
|---|---|---|
| Windows | x86, x64, ARM, ARM64 | ✅ |
| Linux | x64, ARM, ARM64, ARMv8a | ✅ |
| macOS / iOS | 任何 | ❌ 不支持 |

## Critical Constraints

- **设计文档冻结**：`.documents/NET10软件工程设计方案.md` (V5) 是唯一实现依据，改动须走评审
- **防错清单**：实现任何机制前必查 `.documents/NET10实施指导.md` 第 4 章
- **模块纪律**：模块间只经 `Shared/` 接口通信，禁止跨模块引用 DbSet
- **不要 push**：未经允许不得 git push
- **不要改规划**：未经允许不得更改设计文档中的冻结项
- **本机环境**：Windows，MariaDB（属MySQL分支，相对独立），Memurai（Redis 官方合作 Windows 版本），无 docker

## Conventional Commits 规范

**格式**：`<类型>[可选 范围]: <描述>`

### 类型

| 类型 | 说明 | SemVer |
|---|---|---|
| `feat` | 新功能 | MINOR |
| `fix` | 修复 bug | PATCH |
| `docs` | 文档修改 | - |
| `style` | 代码样式修改 | - |
| `refactor` | 重构 | - |
| `perf` | 性能优化 | PATCH |
| `test` | 测试用例 | - |
| `build` | 构建系统/依赖 | - |
| `ci` | CI 配置 | - |
| `chore` | 非业务性修改 | - |

### 范围

可选，用圆括号包围，描述变更的模块：
- `auth` - 认证/授权相关
- `ygg` - Yggdrasil 协议相关
- `admin` - 管理域
- `cache` - Redis/缓存相关
- `db` - 数据库/EF Core
- `api` - API 端点

### 破坏性变更

- 脚注：`BREAKING CHANGE: <描述>`
- 或类型后加 `!`：`feat!: <描述>`

### 示例

```
feat(auth): add Yggdrasil authenticate endpoint
fix(cache): resolve PERM snapshot miss on soft-deleted user
refactor(domain): extract PermissionRules to shared module
docs: update AGENTS.md with commit conventions
test(integration): add concurrent registration test
```

### ⚠️ 颗粒度要求（强制）

| 规则 | 说明 |
|---|---|
| **一个提交只做一件事** | 单个功能、单个修复、单个重构，禁止混合 |
| **精确到功能块** | 如：`feat(auth): register endpoint` 而非 `feat: accounts module` |
| **禁止大杂烩提交** | 不得包含多个不相关的变更（如同时改 DB + 加 API） |
| **提交前自检** | `git diff --cached` 确认只含单一变更，否则拆分 |
| **拆分方法** | `git add -p` 交互式暂存，或多次小提交 |

**反例**：
```
❌ feat: implement accounts module (包含注册+登录+找回+重置)
❌ fix: multiple bugs (修了3个不相关的问题)
```

**正例**：
```
✅ feat(auth): add register endpoint with validation
✅ feat(auth): add login endpoint
✅ fix(auth): resolve race condition in register
```

## Git 分支规范

### 分支模型

| 分支 | 用途 | 命名规范 |
|---|---|---|
| `main` | 生产就绪代码，只接受 PR 合并 | `main` |
| `develop` | 开发主线，集成各功能分支 | `develop` |
| `feature/*` | 新功能开发 | `feature/<模块>-<简述>` |
| `fix/*` | Bug 修复 | `fix/<模块>-<简述>` |
| `hotfix/*` | 生产紧急修复 | `hotfix/<简述>` |
| `release/*` | 发布准备 | `release/<版本号>` |

### 分支命名示例

```
feature/auth-register
feature/ygg-authenticate
feature/admin-player-mgmt
fix/cache-perm-snapshot
fix/db-concurrent-registration
```

### 工作流程

1. **功能开发**：从 `develop` 创建 `feature/*` 分支
2. **完成开发**：PR 合并回 `develop`
3. **发布准备**：从 `develop` 创建 `release/*`，测试通过后合并到 `main` 和 `develop`
4. **热修复**：从 `main` 创建 `hotfix/*`，修复后同时合并到 `main` 和 `develop`

### 提交规范

- 分支内使用约定式提交（见上方 Conventional Commits 规范）
- 合并提交使用 squash merge，保留干净的提交历史

## Architecture

解决方案文件：`FantasyTown.Auth.slnx`

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

| 服务 | 状态 | 用途 |
|---|---|---|
| MariaDB | 正常运行 | 持久化（users/players/player_bans/auth_log 等六表） |
| Memurai | 正常运行 | 令牌/票据/计数/锁定/PERM·PLAYER 快照/DataProtection 密钥环 |

连接串：`appsettings.Development.json` 或环境变量。本机无 docker，使用本地服务。

## Build & Test Commands

```bash
# 还原依赖
dotnet restore

# 构建
dotnet build

# 运行单元测试（无需外部服务）
dotnet test tests/FantasyTown.Auth.UnitTests

# 运行集成测试（需要 MariaDB + Redis 本地服务）
dotnet test tests/FantasyTown.Auth.IntegrationTests

# 运行契约测试
dotnet test tests/FantasyTown.Auth.ContractTests

# EF 迁移
dotnet ef migrations add <MigrationName> --project src/FantasyTown.Auth
dotnet ef database update --project src/FantasyTown.Auth
```

## Testing Conventions

- **单元测试**：纯函数（PermissionRules, IsBanEffective, ManagementGuard），无外部依赖
- **集成测试**：需要 MariaDB + Memurai 本地服务（无 docker，使用本地运行实例）
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
