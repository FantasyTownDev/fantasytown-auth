[中文](README.md) | English

# FantasyTown.Auth

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
> 非 Minecraft 官方产品，未经 Mojang 或 Microsoft 批准，亦不与其关联。
> Operator & responsible party: FantasyTownDev · Contact: [GitHub Issue](https://github.com/FantasyTownDev/fantasytown-auth/issues)

> **AI-Assisted Development Disclaimer**: This project is developed with the assistance of AI language models including Xiaomi MiMo, Kimi, GLM, and DeepSeek. The code is primarily generated and optimized by **Xiaomi MiMo**. See [AI_DECLARATION.md](AI_DECLARATION.md).

Standalone verification-login kernel compatible with the Yggdrasil protocol (for Minecraft third-party login scenarios), built on .NET 10.

## Summary

A minimal standalone verification-login kernel (for Minecraft third-party login scenarios): Yggdrasil protocol for non-Mojang authentication, vertical-slice modular monolith architecture.

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
- Redis 7+ (required); Memurai can substitute on Windows dev

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
  "Site": {
    "Url": "http://example.com:5000"
  },
  "Yggdrasil": {
    "SkinBaseUrl": "http://example.com:5000",
    "SkinDomains": ["example.com"]
  }
}
```

> **SkinBaseUrl Format**: `http://example.com:5000`, `http://example.com`, `https://example.com`, `https://example.com:5000` are all valid. The Minecraft client validates that the texture URL domain is in the `SkinDomains` whitelist.

> **Credential storage**: write production credentials into `appsettings.json` and protect the file with `chmod 600` — see [Production Deployment](#production-deployment). A future release will introduce a locally stored, asymmetrically-encrypted credential config (high-security storage with seamless upgrade compatibility).

### Run

```bash
dotnet run
```

The application starts on `http://0.0.0.0:5000`.

### Health Check

```bash
curl http://localhost:5000/health
```

## Production Deployment

### Publish

```bash
dotnet publish FantasyTown.Auth.csproj -c Release -r linux-x64 --self-contained false -o ./publish
```

Framework-dependent publish (`--self-contained false`) requires the [.NET Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0) on the server; use `--self-contained true` for a self-contained publish that needs no runtime installation.

### Prerequisites (both required)

| Dependency | Notes |
|---|---|
| MariaDB 10.5+ (or MySQL 8.0+) | Create an empty database and grant access — **EF migrations create all tables automatically on first startup**; the database must be reachable at startup (fail-fast: the process exits with a clear error otherwise) |
| Redis 7+ | **Required**: connected at startup; tokens / tickets / rate-limit counters / lockout / DataProtection key ring all live here |

### Configuration

For this release, write production credentials directly into `appsettings.json` and tighten file permissions:

```bash
chmod 600 appsettings.json
```

Required settings:

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | Database connection string (startup fails if left empty) |
| `Redis:Connection` | Redis endpoint, e.g. `localhost:6379` |
| `Redis:Password` | Redis password; leave empty if Redis has none (a security warning is logged at startup) |
| `Site:Url` | Publicly reachable base URL (password-reset links etc. are built from it) |
| `Kestrel:Endpoints:Http:Url` | Listen URL, defaults to `http://0.0.0.0:5000` |

For a MariaDB that only exposes a unix socket on Linux (MySqlConnector 2.4.0):

```
Server=/run/mariadb/mariadb.sock;Database=fantasytown_auth;User ID=your_user;ConnectionProtocol=UnixSocket;
```

The socket path goes into `Server` and `ConnectionProtocol=UnixSocket` must be declared explicitly (this version does not support the `Unix Socket=` key). Any configuration key can also be overridden via environment variables (e.g. `ConnectionStrings__DefaultConnection`) in container / multi-host setups.

### Start & Verify

```bash
dotnet FantasyTown.Auth.dll
curl -f http://localhost:5000/health   # HTTP 200 means ready
```

When using systemd, declare `After=mariadb.service redis-server.service` (or your distribution's equivalents) so the database is already reachable at application startup.

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
├── Persistence/                    # EF Core DbContext
├── Migrations/                     # EF Core initial migration (6-table InitialCreate)
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
- Registration IP quota (default 3 accounts per IP; requests over quota are rejected)
- Failed-login lockout with exponential backoff (15 / 30 / 60 minutes)
- Immediate banned-user interception (PERM snapshot + RejectBannedUser middleware)
- RSA-SHA1 texture signing (authlib-injector compatible)

## Known Limitations

- **Email channel in development**: the password-reset token pipeline is ready, but mail delivery is not yet wired up — submitting the forgot-password form currently sends no email. For an urgent reset, update the password hash in the database directly (Argon2 PHC format). Mail delivery is on the roadmap
- **Admin UI in development**: `/admin` pages ship with the P4 milestone; currently bans and permission changes require direct database operations

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
- [x] Unit tests all passing

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

### P3.5: Pre-Admin Hardening (Done)

- [x] Stateless password-reset tokens (DataProtection time-limited protection, per frozen design §4.5)
- [x] Registration IP quota (REG_IP snapshot counting)
- [x] Credential hardcoding removed (connection-string defaults cleared; missing config fails fast)
- [x] Atomic concurrent saves (registration race committed in a single SaveChanges)
- [x] Linux x64 production verification (MariaDB unix socket + Redis)

### P4: Admin Domain (In Progress)

- [x] ManagementGuard three functions (CanManage / NotSelf / CanClearLockout)
- [ ] /admin authorization policies (moderator / owner / admin)
- [ ] /admin area global no-cache middleware
- [ ] Management pipeline (Antiforgery > Policy > Privilege Matrix > Double-Confirm > Domain Command > Side Effects > Audit > PRG)
- [ ] Sensitive operation double-confirm (permission-change / force-reset-password / delete require actor password)
- [ ] /admin player management pages (list / detail / ban / unban / reset-texture / rename)
- [ ] /admin user management pages (list / detail / ban / unban / permission / force-reset-password / email / soft-delete)
- [ ] /admin moderator management pages (list)
- [ ] /admin lockout management pages (clear lockout)
- [x] ManagementGuard unit tests (CanManage full combinations + CanClearLockout self-pass anchor)
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

Released under [AGPL-3.0-only](LICENSE) (single bilingual license file; the verbatim English text is canonical).

- Additional terms: [NOTICE.md](NOTICE.md) (AGPLv3 §7: attribution, modified-version identification, source obligation)
- Trademark & naming: [TRADEMARK.md](TRADEMARK.md) (written-permission scheme for the FantasyTown name; Minecraft/Yggdrasil brand boundaries)
- Commercial cooperation: [COMMERCIAL.md](COMMERCIAL.md) (independent contract offer; does not alter AGPLv3 rights)
- Copyright inventory: [COPYRIGHT](COPYRIGHT) · AI declaration: [AI_DECLARATION.md](AI_DECLARATION.md)
