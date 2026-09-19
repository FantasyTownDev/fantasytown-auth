# PR: feature/ygg-authenticate -> develop

## Title

```
feat: implement P0-P3 Yggdrasil authentication kernel (authserver + sessionserver + skin service)
```

## Description

### Summary

Complete implementation of the Minecraft third-party authentication kernel covering milestones P0 through P3. This branch delivers a fully functional Yggdrasil-compatible authentication server with authlib-injector support, skin texture service, and web account system.

### What's Included

**P0 - Infrastructure**
- EF Core with MariaDB (6 tables, initial migration)
- Redis integration (tokens, tickets, counters, lockout, permission snapshots, DataProtection key ring)
- Razor Pages (login, register, forgot-password, reset-password)
- OpenTelemetry distributed tracing
- Health check endpoint (`/health` with DB + Redis)
- Cookie authentication (`__Host-ft_auth`)

**P1 - Account System**
- Registration with concurrent-safety (INSERT+catch for race conditions)
- Login with ban/lockout checks
- Password recovery and reset with security_stamp rotation
- User enumeration prevention (uniform response + timing equalization)
- Atomic token issuance via Redis Lua scripts
- Three-layer rate limiting (IP fixed window, account x IP sliding window, concurrency permit)
- Immediate ban interception (RejectBannedUserMiddleware + PERM snapshot)
- Soft-delete with player name release
- 203 unit tests all passing

**P2 - Yggdrasil Authserver**
- authenticate / validate / refresh / invalidate / signout endpoints
- RedisTokenService with Lua atomic operations
- authlib-injector compatibility (metadata endpoint, selectedProfile adapter, signature format)
- Yggdrasil Protocol DTOs with System.Text.Json source generation
- Golden fixture byte-for-byte contract tests

**P3 - Sessionserver**
- join / hasJoined / profile endpoints
- RSA-SHA1 texture signing (authlib-injector compatible)
- Skin texture serving with `Cache-Control: max-age=0, must-revalidate`
- RedisPlayerCache for server-wide synchronized visibility
- authlib-injector signature: signs only `propertyValue` bytes per `verifyPropertySignature` source

### Key Technical Decisions

1. **Redis password handling**: Uses `ConfigurationOptions` object construction (not string parsing) to avoid `@` delimiter issues in passwords
2. **authlib-injector compatibility**: Signs only `valueBase64` per `verifyPropertySignature` (not `name + value`)
3. **Skin texture URL**: Registered before HTTPS redirect for Minecraft client compatibility
4. **UUID normalization**: Accepts uppercase from client, normalizes to lowercase for file lookup

### Testing

- Unit tests: 203 passing (PermissionRules, BanRules, RateLimiters, TokenService, etc.)
- Contract tests: Yggdrasil protocol golden fixtures (byte-for-byte match)
- Integration tests: P2/P3 acceptance gate tests (require MariaDB + Redis)
- Skin signing: verified in production environment

### Merge Strategy

Squash merge recommended to keep develop history clean.
