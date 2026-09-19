[中文](README.md) | English

# FantasyTown.Auth

> **AI-Assisted Development Disclaimer**: This project is developed with the assistance of AI language models including Xiaomi MiMo, Kimi, GLM, and DeepSeek. The code is primarily generated and optimized by **Xiaomi MiMo**.

Minimal kernel for Minecraft third-party authentication (Yggdrasil external login + web account registration), built on .NET 10.

## Summary

A minimal Minecraft third-party authentication kernel: Yggdrasil protocol for non-Mojang authentication, vertical-slice modular monolith architecture.

## Features

- **Yggdrasil Third-Party Authentication**: Full protocol support (authenticate / validate / refresh / invalidate / signout / join / hasJoined / profile)
- **authlib-injector Compatibility**: Supports both standard Yggdrasil protocol and authlib-injector standard
- **Skin Service**: RSA signing, server-wide synchronized visibility (identical to Minecraft official scheme)
- **Web Account System**: Email registration + unique Minecraft player name binding
- **Four-Tier Permission Model**: NormalPlayer(0) > ServerModerator(1) > ServerOwner(2) > PlatformAdmin(3)
- **Two-Level Bans**: Account-level (affects login) + Player-level (bans individual player name)
- **High-Concurrency Design**: Zero DB access on hot paths, single instance steady-state 5000 RPS
- **Security Design**: User enumeration prevention, atomic token issuance, single-use tickets
- **Extensible Architecture**: Modular design for easy feature extension

## Tech Stack

- **Framework**: .NET 10 / ASP.NET Core 10
- **ORM**: EF Core 9.0 (Pomelo.EntityFrameworkCore.MySql 9.0.0)
- **Cache**: Redis 7+ (recommended) / Memurai (Windows dev only, not recommended for production)
- **Database**: MariaDB 10.5+ (11.x LTS recommended, 12.x compatible but not officially tested by Pomelo). Also compatible with MySQL 8.0+
- **Frontend**: Razor Pages + Minimal API
- **Language**: C# 14

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MariaDB 10.5+ (11.x LTS recommended, 12.x compatible but not officially tested by Pomelo)
- Redis 7+ (recommended) or Memurai (Windows dev only)

### Configuration

Edit `appsettings.json`:

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
    "SkinBaseUrl": "http://example.com:5000",
    "SkinDomains": ["example.com"]
  }
}
```

> **SkinBaseUrl Format**: `http://example.com:5000`, `http://example.com`, `https://example.com`, `https://example.com:5000` are all valid. The Minecraft client validates that the texture URL domain is in the `SkinDomains` whitelist.

### Run

```bash
dotnet run
```

The application starts on `http://0.0.0.0:5000`.

### Health Check

```bash
curl http://localhost:5000/health
```

## Project Structure

```
FantasyTown.Auth/
├── Program.cs                      # Startup config, DI, middleware pipeline
├── Modules/
│   ├── Accounts/                   # Account module
│   │   ├── Application/            # Application layer: command/query handlers
│   │   ├── Domain/                 # Domain layer: entities/value objects/rules
│   │   └── Infrastructure/         # Infrastructure: password service/permission snapshot/lockout
│   ├── Yggdrasil/                  # Yggdrasil module
│   │   ├── Authserver/             # authenticate/validate/refresh/invalidate/signout
│   │   ├── Sessions/               # join/hasJoined/profile + TicketService + signing
│   │   └── Protocol/               # Frozen DTOs + JsonSourceGeneration
│   └── Shared/                     # Shared: Result, IClock, IClientIp, IAuditChannel
├── Persistence/                    # EF Core DbContext, migrations
├── Middleware/                     # RejectBannedUser middleware
└── Pages/                          # Razor Pages: login/register/forgot-password/reset-password
```

## API Endpoints

### Yggdrasil Authserver

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/yggdrasil/authserver/authenticate` | Authentication login |
| POST | `/api/yggdrasil/authserver/validate` | Token validation |
| POST | `/api/yggdrasil/authserver/refresh` | Token refresh |
| POST | `/api/yggdrasil/authserver/invalidate` | Token invalidation |
| POST | `/api/yggdrasil/authserver/signout` | Sign out (revoke all) |

### Yggdrasil Sessionserver

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/yggdrasil/sessionserver/session/minecraft/join` | Player join |
| GET | `/api/yggdrasil/sessionserver/session/minecraft/hasJoined` | Verify join |
| GET | `/api/yggdrasil/sessionserver/session/minecraft/profile/{uuid}` | Query profile |

### Other

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/yggdrasil` | authlib-injector metadata |
| GET | `/textures/skins/{uuid}.png` | Skin texture |
| GET | `/health` | Health check |

## Configuration

### Redis Password

If Redis has a password configured, set it in `appsettings.json`:

```json
{
  "Redis": {
    "Connection": "localhost:6379",
    "Password": "your_redis_password"
  }
}
```

### Skin Domains

`Yggdrasil:SkinDomains` configures the skin domain whitelist. The Minecraft client validates that texture URLs belong to a domain in this list.

## Security Features

- User enumeration prevention (uniform response + timing equalization)
- Atomic token issuance (Redis Lua scripts)
- Tiered rate limiting (IP fixed window + account x IP sliding window)
- Immediate banned-user interception (PERM snapshot + RejectBannedUser middleware)
- RSA-SHA1 texture signing (authlib-injector compatible)

## Roadmap

### P0: Infrastructure (Done)

- [x] EF Core DbContext + initial migration (6 tables + UNIQUE constraints)
- [x] Redis integration (tokens / tickets / counters / lockout / permission snapshots / DataProtection key ring)
- [x] Razor Pages (login / register / forgot-password / reset-password)
- [x] OpenTelemetry observability
- [x] /health endpoint (DB + Redis)
- [x] Cookie authentication (`__Host-ft_auth`)
- [x] Test fixture framework (contract tests/fixtures)

### P1: Account System (Done)

- [x] Registration (RegisterUserCommand + Razor Page)
- [x] Login (LoginQuery + Razor Page)
- [x] Forgot password (ForgotPassword Page)
- [x] Reset password (PasswordResetCommand + Razor Page)
- [x] User enumeration prevention (uniform response + timing equalization)
- [x] Atomic token issuance (Redis Lua scripts)
- [x] Tiered rate limiting (IP fixed window + account x IP sliding window + concurrency limiter)
- [x] Immediate ban interception (RejectBannedUserMiddleware + PERM snapshot)
- [x] Lockout service (RedisLockoutService)
- [x] Soft-delete users (SoftDeleteUserCommand)
- [x] Concurrent registration = exactly 1 (unit test coverage)
- [x] Permission rules (four-tier enum + whitelist exact match)
- [x] Ban rules (BanRules + IsBanEffective time predicate)
- [x] 203 unit tests all passing

### P2: Yggdrasil Authserver (Done)

- [x] authenticate endpoint (AuthenticateEndpoint + Handler)
- [x] validate endpoint (ValidateEndpoint + Handler)
- [x] refresh endpoint (RefreshEndpoint + Handler)
- [x] invalidate endpoint (InvalidateEndpoint + Handler)
- [x] signout endpoint (SignoutEndpoint + Handler)
- [x] Token service (RedisTokenService + Lua scripts)
- [x] authlib-injector compatibility (metadata endpoint + selectedProfile adapter + signature format)
- [x] Yggdrasil Protocol DTOs (JsonSourceGeneration)
- [x] golden fixture byte-for-byte match (ContractTests)

### P3: Sessionserver (Done)

- [x] join endpoint (JoinEndpoint + JoinHandler + TicketService)
- [x] hasJoined endpoint (HasJoinedEndpoint + HasJoinedHandler)
- [x] profile endpoint (ProfileEndpoint + ProfileHandler)
- [x] RSA-SHA1 texture signing (RsaSigningService + ExportSubjectPublicKeyInfoPem)
- [x] Skin texture serving (TextureEndpoint + texture URL generation)
- [x] Player cache (RedisPlayerCache)
- [x] Server-wide synchronized visibility (texture pass-back verification)
- [x] hasJoined P99 < 50ms

### P4: Admin Domain (Pending)

- [ ] ManagementGuard three functions (CanManage / CanClearLockout / CanExecuteAction)
- [ ] /admin authorization policies (moderator / owner / admin)
- [ ] /admin area global no-cache middleware
- [ ] Management pipeline (Antiforgery > Policy > Privilege Matrix > Double-Confirm > Domain Command > Side Effects > Audit > PRG)
- [ ] Sensitive operation double-confirm (permission-change / force-reset-password / delete require actor password)
- [ ] /admin player management pages (list / detail / ban / unban / reset-texture / rename)
- [ ] /admin user management pages (list / detail / ban / unban / permission / force-reset-password / email / soft-delete)
- [ ] /admin moderator management pages (list)
- [ ] /admin lockout management pages (clear lockout)
- [ ] ManagementGuard unit tests (CanManage full combinations + CanClearLockout self-pass anchor)
- [ ] Admin domain regression tests (privilege matrix full coverage + double-confirm + audit persistence)

### P5: Hardening & Stress Test (Pending)

- [ ] HSTS / security response headers
- [ ] Non-root container deployment
- [ ] validate 5k RPS steady state (DB QPS approx 0)
- [ ] Stress test report

### Future Work

- [ ] Post-login features (upload/change skin, change player name)
- [ ] Trim unnecessary database schema
- [ ] (Possible) External player/account status query and ban request API
- [ ] (Possible) Official account binding, unified official/unofficial authentication

## License

AGPL-3.0 with non-commercial use restrictions. See [LICENSE](LICENSE_en).
