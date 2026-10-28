中文 | [English](README_en.md)

# FantasyTown.Auth

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
> 非 Minecraft 官方产品，未经 Mojang 或 Microsoft 批准，亦不与其关联。
> 运营与责任主体：FantasyTownDev · 联系方式：[GitHub Issue](https://github.com/FantasyTownDev/fantasytown-auth/issues)

> **AI 辅助编程声明**：本项目采用 AI 辅助编程开发，涉及 Xiaomi MiMo、Kimi、GLM、DeepSeek 等大语言模型，其中代码主要由 **Xiaomi MiMo** 全权负责生成与优化。详见 [AI_DECLARATION.md](AI_DECLARATION.md)。

兼容 Yggdrasil 协议的独立验证登录内核（适用于 Minecraft 第三方登录场景），基于 .NET 10 构建。

## 一句话定位

一个最小化的第三方验证登录内核（适用于 Minecraft 第三方登录场景）：基于 Yggdrasil 协议实现非 Mojang 官方验证，采用垂直切片模块化单体架构。

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

- **框架**：.NET 10 / ASP.NET Core 10
- **ORM**：EF Core 9.0 (Pomelo.EntityFrameworkCore.MySql 9.0.0)
- **缓存**：Redis 7+（推荐）/ Memurai（Windows 开发环境可选，不推荐生产部署）
- **数据库**：MariaDB 10.5+（推荐 11.x LTS，12.x 兼容但未经 Pomelo 官方测试）。也兼容 MySQL 8.0+
- **前端**：Razor Pages + Minimal API
- **语言**：C# 14

## 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MariaDB 10.5+（推荐 11.x LTS，12.x 兼容但未经 Pomelo 官方测试）
- Redis 7+（必需）；Windows 开发环境可用 Memurai 替代

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
  "Site": {
    "Url": "http://example.com:5000"
  },
  "Yggdrasil": {
    "SkinBaseUrl": "http://example.com:5000",
    "SkinDomains": ["example.com"]
  }
}
```

> **SkinBaseUrl 格式**：`http://example.com:5000`、`http://example.com`、`https://example.com`、`https://example.com:5000` 均可。Minecraft 客户端会校验纹理 URL 域名是否在 `SkinDomains` 白名单中。

> **凭据存放**：生产环境凭据写入 `appsettings.json` 并以 `chmod 600` 收紧文件权限，详见[生产部署](#生产部署)；后续版本计划提供本地非对称可逆加密的凭据配置方案（高安全性存储、兼容平滑更新）。

### 运行应用

```bash
dotnet run
```

应用将在 `http://0.0.0.0:5000` 启动。

### 健康检查

```bash
curl http://localhost:5000/health
```

## 生产部署

### 发布

```bash
dotnet publish FantasyTown.Auth.csproj -c Release -r linux-x64 --self-contained false -o ./publish
```

`--self-contained false` 为框架依赖发布，服务器需安装 [.NET 运行时 10](https://dotnet.microsoft.com/download/dotnet/10.0)；服务器不便安装运行时时改用 `--self-contained true`（自包含发布，产物更大、免装运行时）。

### 前置依赖（均为必需）

| 依赖 | 说明 |
|---|---|
| MariaDB 10.5+（或 MySQL 8.0+） | 建空库并授权即可，**应用启动时自动执行 EF 迁移建表**；启动瞬间数据库必须可达（fail-fast，DB 未就绪则进程退出并给出明确错误） |
| Redis 7+ | **必需**：启动即连接；令牌 / 票据 / 限流计数 / 锁定 / DataProtection 密钥环全部托管其上 |

### 配置

当前版本凭据直接写入 `appsettings.json`，上线前收紧文件权限：

```bash
chmod 600 appsettings.json
```

必需配置项：

| 键 | 说明 |
|---|---|
| `ConnectionStrings:DefaultConnection` | 数据库连接串（留空启动即失败） |
| `Redis:Connection` | Redis 地址，如 `localhost:6379` |
| `Redis:Password` | Redis 密码；Redis 未设密码则留空（启动时会输出安全警告日志） |
| `Site:Url` | 对外访问地址（密码重置链接等绝对 URL 以此为基址） |
| `Kestrel:Endpoints:Http:Url` | 监听地址，默认 `http://0.0.0.0:5000` |

Linux 下 MariaDB 仅开放 unix socket 时，连接串写法（MySqlConnector 2.4.0）：

```
Server=/run/mariadb/mariadb.sock;Database=fantasytown_auth;User ID=your_user;ConnectionProtocol=UnixSocket;
```

socket 路径写入 `Server`，并显式声明 `ConnectionProtocol=UnixSocket`（该版本不支持 `Unix Socket=` 键）。容器 / 多机场景亦可用环境变量覆盖任意配置键（如 `ConnectionStrings__DefaultConnection`）。

### 启动与验证

```bash
dotnet FantasyTown.Auth.dll
curl -f http://localhost:5000/health   # 返回 200 即就绪
```

使用 systemd 时建议声明 `After=mariadb.service redis-server.service`（或发行版等价单元），确保应用启动时数据库已可达。

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
├── Persistence/                    # EF Core DbContext
├── Migrations/                     # EF Core 初始迁移（六表 InitialCreate）
├── Middleware/                     # RejectBannedUser 中间件
└── Pages/                          # Razor Pages：登录/注册/找回/重置
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
- 注册 IP 配额（默认每 IP 3 个账户，超出拒绝）
- 登录失败锁定（指数退避 15 / 30 / 60 分钟）
- 封禁用户即时拦截（PERM 快照 + RejectBannedUser 中间件）
- RSA-SHA1 纹理签名（authlib-injector 兼容）

## 已知限制

- **邮件通道开发中**：密码重置令牌链路已就绪，但邮件发送尚未接入——当前提交"忘记密码"表单不会发出邮件；急需重置某账户密码时，可更新数据库中的密码哈希（Argon2 PHC 格式）。邮件接入已列入 Roadmap
- **管理后台开发中**：`/admin` 管理页面随 P4 里程碑交付；当前版本封禁 / 权限调整等需直接操作数据库

## TODO List

### P0：基础设施 ✅

- [x] EF Core DbContext + 初始迁移（6 表结构 + UNIQUE 约束）
- [x] Redis 集成（令牌 / 票据 / 计数 / 锁定 / 权限快照 / DataProtection 密钥环）
- [x] Razor Pages（登录 / 注册 / 找回 / 重置）
- [x] OpenTelemetry 可观测性
- [x] /health 健康检查（DB + Redis）
- [x] Cookie 认证（`__Host-ft_auth`）
- [x] 测试 fixture 框架（contract tests/fixtures）

### P1：账户系统 ✅

- [x] 注册（RegisterUserCommand + Razor Page）
- [x] 登录（LoginQuery + Razor Page）
- [x] 找回密码（ForgotPassword Page）
- [x] 重置密码（PasswordResetCommand + Razor Page）
- [x] 用户枚举防护（统一话术 + 等耗时校验）
- [x] 令牌原子签发（Redis Lua 脚本）
- [x] 分层限流（IP 固定窗 + 账户×IP 滑动窗 + 并发认证限制）
- [x] 封禁即时拦截（RejectBannedUserMiddleware + PERM 快照）
- [x] 锁定服务（RedisLockoutService）
- [x] 软删用户（SoftDeleteUserCommand）
- [x] 并发注册恰 1（单元测试覆盖）
- [x] 权限判定（PermissionRules 四级枚举 + 白名单精确匹配）
- [x] 封禁判定（BanRules + IsBanEffective 时间谓词）
- [x] 单元测试全部通过

### P2：Yggdrasil Authserver ✅

- [x] authenticate 端点（AuthenticateEndpoint + Handler）
- [x] validate 端点（ValidateEndpoint + Handler）
- [x] refresh 端点（RefreshEndpoint + Handler）
- [x] invalidate 端点（InvalidateEndpoint + Handler）
- [x] signout 端点（SignoutEndpoint + Handler）
- [x] Token 服务（RedisTokenService + Lua 脚本）
- [x] authlib-injector 兼容（metadata 端点 + selectedProfile 适配 + 签名格式）
- [x] Yggdrasil Protocol DTO（JsonSourceGeneration）
- [x] golden fixture 逐字节一致（ContractTests）

### P3：Sessionserver ✅

- [x] join 端点（JoinEndpoint + JoinHandler + TicketService）
- [x] hasJoined 端点（HasJoinedEndpoint + HasJoinedHandler）
- [x] profile 端点（ProfileEndpoint + ProfileHandler）
- [x] RSA-SHA1 纹理签名（RsaSigningService + ExportSubjectPublicKeyInfoPem）
- [x] 皮肤纹理服务（TextureEndpoint + 纹理 URL 生成）
- [x] 玩家缓存（RedisPlayerCache）
- [x] 全服同步可见（纹理回传验证）
- [x] hasJoined P99 < 50ms

### P3.5：前置修复 ✅

- [x] 密码重置令牌无状态化（DataProtection 限时保护，符合冻结设计 §4.5）
- [x] 注册 IP 配额（REG_IP 快照计数）
- [x] 配置凭据硬编码清除（连接串等默认值置空，缺失即 fail-fast）
- [x] 并发保存原子化（注册竞态单次 SaveChanges 原子提交）
- [x] Linux x64 生产环境实机验证（MariaDB socket + Redis）

### P4：管理域（进行中）

- [x] ManagementGuard 三函数（CanManage / NotSelf / CanClearLockout）
- [ ] /admin 授权策略（moderator / owner / admin）
- [ ] /admin 区域全域 no-cache 中间件
- [ ] 管理管线（Antiforgery → 策略 → 越权矩阵 → 二次确认 → 领域命令 → 副作用 → 审计 → PRG）
- [ ] 敏感操作二次确认（permission-change / force-reset-password / delete 需 actor 密码）
- [ ] 种子邮箱首个注册者自动提升为服主
- [ ] /admin 玩家管理页（列表 / 详情 / 封禁 / 解封 / 重置纹理 / 改名）
- [ ] /admin 用户管理页（列表 / 详情 / 封禁 / 解封 / 权限变更 / 强制重置密码 / 邮箱改绑 / 软删）
- [ ] /admin 协管管理页（列表）
- [ ] /admin 锁定管理页（清除锁定）
- [x] ManagementGuard 单测（CanManage 全组合 + CanClearLockout 自我放行锚点）
- [ ] 管理域回归测试（越权矩阵全覆盖 + 二次确认 + 审计落库）

### P5：加固压测

- [ ] HSTS / 安全响应头
- [ ] 容器非 root 部署
- [ ] validate 5k RPS 稳态（DB QPS ≈ 0）
- [ ] 压测报告

### 后续待定

- [ ] 完善用户登录后功能（上传 / 更换皮肤、更改玩家名）
- [ ] 裁剪不必要的数据库规划
- [ ] 【可能的】外部玩家 / 账号状态查询和封禁请求 API
- [ ] 【可能的】绑定正版账号，兼容正版 / 非正版统一验证

## 许可证

本项目以 [AGPL-3.0-only](LICENSE) 释出（单一中英混排许可证文件，正式条款以随附英文原文为准）。

- 附加材料条款：[NOTICE.md](NOTICE.md)（AGPLv3 §7：署名保留、修改版标注、源码义务）
- 商标与命名：[TRADEMARK.md](TRADEMARK.md)（FantasyTown 名号书面申请制；Minecraft/Yggdrasil 品牌边界）
- 商务合作：[COMMERCIAL.md](COMMERCIAL.md)（独立合同要约，不改变 AGPLv3 授权范围）
- 版权清单：[COPYRIGHT](COPYRIGHT) · AI 辅助声明：[AI_DECLARATION.md](AI_DECLARATION.md)
