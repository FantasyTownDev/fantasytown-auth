# FantasyTown.Auth

Minecraft 第三方验证登录系统最小化内核，基于 .NET 10 构建。

## 一句话定位

一个最小化的 Minecraft 第三方验证登录系统内核：基于 Yggdrasil 协议实现非 Mojang 官方验证，支持绑定正版账号以兼容正版/非正版统一验证，采用垂直切片模块化单体架构。

## 功能特性

- **Yggdrasil 第三方验证**：完整协议支持（authenticate/validate/refresh/invalidate/signout/join/hasJoined/profile）
- **皮肤服务**：材质上传、RSA 签名、预计算缓存、全服可见（与 Minecraft 官方方案一致）
- **Web 账户系统**：邮箱注册 + 唯一 Minecraft 角色名绑定
- **四级权限体系**：普通玩家(0) → 协管(1) → 服主(2) → 平台管理员(3)
- **两级封禁**：账户级（影响登录）+ 玩家级（禁单个角色名）
- **管理域**：Razor Pages `/admin` 管理后台
- **高并发设计**：热路径零 DB 访问，单实例稳态 5000 RPS
- **安全设计**：免用户枚举、令牌原子、票据一次性
- **可扩展架构**：模块化设计，便于后续功能扩展

## 技术栈

- **框架**：.NET 10 (LTS) / ASP.NET Core 10
- **ORM**：EF Core 10 (Pomelo for MySQL)
- **缓存**：Redis 7+ / HybridCache
- **数据库**：MySQL 8.0+ / PostgreSQL (可选)
- **前端**：Razor Pages + Minimal API
- **语言**：C# 14

## 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (用于 MySQL 和 Redis)

### 启动外部服务

```bash
# 启动 MySQL
docker run -d -p 3306:3306 -e MYSQL_ROOT_PASSWORD=dev -e MYSQL_DATABASE=fantasytown_auth mysql:8

# 启动 Redis
docker run -d -p 6379:6379 redis:7
```

### 运行应用

```bash
dotnet run
```

应用将在 `https://localhost:5001` 启动。

### 健康检查

```bash
curl https://localhost:5001/health
```

## 项目结构

```
FantasyTown.Auth/
├── Program.cs                      # 启动配置
├── Modules/
│   ├── Accounts/                   # 账户模块
│   │   ├── Web/                    # Razor Pages：注册/登录/找回/重置
│   │   ├── Admin/                  # Razor Pages：/admin/* 管理后台
│   │   ├── Application/            # 应用层：命令/查询
│   │   ├── Domain/                 # 领域层：实体/值对象/规则
│   │   └── Infrastructure/         # 基础设施：密码服务/权限快照/重置令牌
│   ├── Yggdrasil/                  # Yggdrasil 模块
│   │   ├── Authserver/             # authenticate/validate/refresh/invalidate/signout
│   │   ├── Sessions/               # join/hasJoined + TicketService
│   │   ├── Profiles/               # 材质组装 + RSA 签名 + 预计算缓存
│   │   └── Protocol/               # 冻结 DTO + JsonSourceGeneration
│   └── Shared/                     # 共享：Result, IClock, IClientIp, IAuditChannel
└── Persistence/                    # EF Core DbContext, 迁移
```

## 开发指南

### 数据库迁移

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

### 运行测试

```bash
dotnet test
```

### 站点引导

首位服主通过以下方式产生：
1. **种子邮箱**：配置 `seed_owner_email` 环境变量，首个使用该邮箱注册的用户自动成为服主
2. **CLI 命令**：`dotnet run -- promote-owner {email}`

## 配置

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=fantasytown_auth;User=root;Password=dev;"
  },
  "Redis": {
    "Connection": "localhost:6379"
  },
  "Site": {
    "Url": "https://localhost:5001",
    "RegsPerIp": 3,
    "PlayerNameRule": "cjk",
    "PlayerNameLength": "3-16"
  },
  "Yggdrasil": {
    "TokenExpire1": "3.00:00:00",
    "TokenExpire2": "7.00:00:00",
    "AuthRateLimitPerIp": 30,
    "UuidAlgorithm": "v3"
  }
}
```

### 环境变量

- `SEED_OWNER_EMAIL`：种子邮箱（容器 Secret）
- `YGG_PRIVATE_KEY`：RSA 私钥路径
- `ConnectionStrings__DefaultConnection`：数据库连接串
- `Redis__Connection`：Redis 连接串

## 安全特性

- 用户枚举防护（统一话术 + 等耗时校验）
- IP 伪造防护（ForwardedHeaders + KnownProxies）
- 令牌原子签发（Lua 脚本）
- 分层限流（IP 固定窗 + 账户×IP 滑动窗 + 指数退避锁定）
- 敏感操作二次确认（actor 当前密码验证）
- Web 会话封禁即时性（PERM 快照 + RejectBannedUser 中间件）

## 文档

详细设计文档见项目根目录 `.documents/` 文件夹。

## 许可证

MIT License
