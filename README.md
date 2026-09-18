# FantasyTown.Auth

> **AI 辅助编程声明**：本项目采用 AI 辅助编程开发，涉及 Xiaomi MiMo、Kimi、GLM、DeepSeek 等大语言模型，其中代码主要由 **Xiaomi MiMo** 全权负责生成与优化。

Minecraft 第三方验证登录系统最小化内核，基于 .NET 10 构建。

## 一句话定位

一个最小化的 Minecraft 第三方验证登录系统内核：基于 Yggdrasil 协议实现非 Mojang 官方验证，采用垂直切片模块化单体架构。

## 功能特性

- **Yggdrasil 第三方验证**：完整协议支持（authenticate/validate/refresh/invalidate/signout/join/hasJoined/profile）
- **authlib-injector 兼容**：同时支持标准 Yggdrasil 协议和 authlib-injector 标准
- **皮肤服务**：RSA 签名、全服同步可见（与 Minecraft 官方方案一致）
- **Web 账户系统**：邮箱注册 + 唯一 Minecraft 角色名绑定
- **四级权限体系**：普通玩家(0) → 协管(1) → 服主(2) → 平台管理员(3)
- **两级封禁**：账户级（影响登录）+ 玩家级（禁单个角色名）
- **高并发设计**：热路径零 DB 访问，单实例稳态 5000 RPS
- **安全设计**：免用户枚举、令牌原子、票据一次性
- **可扩展架构**：模块化设计，便于后续功能扩展

## 技术栈

- **框架**：.NET 10 (LTS) / ASP.NET Core 10
- **ORM**：EF Core 9.0 (Pomelo.EntityFrameworkCore.MySql 9.0.0)
- **缓存**：Redis 7+（推荐）/ Memurai（Windows 开发环境可选，不推荐生产部署）
- **数据库**：MariaDB 12.3.3
- **前端**：Razor Pages + Minimal API
- **语言**：C# 14

## 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MariaDB 12.3.3
- Redis 7+（推荐）或 Memurai（Windows 开发环境可选）

### 配置

编辑 `appsettings.json`：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=fantasytown_auth;User=root;Password=your_password;"
  },
  "Redis": {
    "Connection": "localhost:6379",
    "Password": ""
  },
  "Yggdrasil": {
    "SkinBaseUrl": "http://your-server-ip:5000",
    "SkinDomains": ["your-domain.com"]
  }
}
```

### 运行应用

```bash
dotnet run
```

应用将在 `http://0.0.0.0:5000` 启动。

### 健康检查

```bash
curl http://localhost:5000/health
```

## 项目结构

```
FantasyTown.Auth/
├── Program.cs                      # 启动配置、DI、中间件管道
├── Modules/
│   ├── Accounts/                   # 账户模块
│   │   ├── Application/            # 应用层：命令/查询处理器
│   │   ├── Domain/                 # 领域层：实体/值对象/规则
│   │   └── Infrastructure/         # 基础设施：密码服务/权限快照/锁定服务
│   ├── Yggdrasil/                  # Yggdrasil 模块
│   │   ├── Authserver/             # authenticate/validate/refresh/invalidate/signout
│   │   ├── Sessions/               # join/hasJoined/profile + TicketService + 签名
│   │   └── Protocol/               # 冻结 DTO + JsonSourceGeneration
│   └── Shared/                     # 共享：Result, IClock, IClientIp, IAuditChannel
├── Persistence/                    # EF Core DbContext, 迁移
├── Middleware/                     # RejectBannedUser 中间件
├── Pages/                          # Razor Pages：登录/注册/找回/重置
└── textures/skins/                 # 皮肤文件存储目录
```

## API 端点

### Yggdrasil Authserver

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/yggdrasil/authserver/authenticate` | 认证登录 |
| POST | `/api/yggdrasil/authserver/validate` | 令牌验证 |
| POST | `/api/yggdrasil/authserver/refresh` | 令牌刷新 |
| POST | `/api/yggdrasil/authserver/invalidate` | 令牌注销 |
| POST | `/api/yggdrasil/authserver/signout` | 登出 revoke all |

### Yggdrasil Sessionserver

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/yggdrasil/sessionserver/session/minecraft/join` | 玩家加入 |
| GET | `/api/yggdrasil/sessionserver/session/minecraft/hasJoined` | 验证加入 |
| GET | `/api/yggdrasil/sessionserver/session/minecraft/profile/{uuid}` | 查询档案 |

### 其他

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/yggdrasil` | authlib-injector 元数据 |
| GET | `/textures/skins/{uuid}.png` | 皮肤纹理 |
| GET | `/health` | 健康检查 |

## 配置说明

### Redis 密码

如果 Redis 设置了密码，在 `appsettings.json` 中配置：

```json
{
  "Redis": {
    "Connection": "localhost:6379",
    "Password": "your_redis_password"
  }
}
```

### 皮肤域名

`Yggdrasil:SkinDomains` 配置皮肤域名白名单，Minecraft 客户端会校验纹理 URL 是否在此列表中。

## 安全特性

- 用户枚举防护（统一话术 + 等耗时校验）
- 令牌原子签发（Redis Lua 脚本）
- 分层限流（IP 固定窗 + 账户×IP 滑动窗）
- 封禁用户即时拦截（PERM 快照 + RejectBannedUser 中间件）
- RSA-SHA1 纹理签名（authlib-injector 兼容）

## TODO List

- [x] P0：EF Core / Redis / 页面 / 可观测性 + fixture 框架
- [x] P1：Accounts 注册 / 登录 / 找回 / 重置 + 安全修复
- [x] P2：Yggdrasil Authserver（authenticate / validate / refresh / invalidate / signout）
- [x] P3：Sessionserver（join / hasJoined / profile）+ 皮肤签名 + 全服可见
- [x] authlib-injector 协议适配（join selectedProfile 兼容、metadata 端点、签名格式）
- [x] MariaDB 数据库支持（6 表结构 + 迁移）
- [x] Redis 缓存集成（令牌 / 票据 / 计数 / 锁定 / 权限快照）
- [x] 单元测试 203 项全部通过
- [x] 皮肤签名在生产环境验证通过
- [ ] 完善用户登录后功能：支持上传和更换玩家皮肤、更改玩家名等基础功能
- [ ] 裁剪不必要的数据库规划，仅保留基础验证功能所需的表和字段
- [ ] 完善管理域（ManagementGuard、越权矩阵）
- [ ] 开发管理后台页面（/admin），用于站点管理和玩家封禁管理
- [ ] 【可能的】保留来自外部的玩家 / 账号状态查询和玩家封禁请求 API 接口
- [ ] 【可能的】绑定正版账号，兼容正版 / 非正版统一验证

## 许可证

AGPL-3.0 + 非商业使用附加条款。详见 [LICENSE](LICENSE)。
