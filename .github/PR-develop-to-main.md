# PR: develop -> main

## Title

```
release: v1.0.0 - Yggdrasil authentication kernel (P0-P3)
```

## Description

### Summary

First stable release of FantasyTown.Auth covering milestones P0 through P3. This release delivers a production-ready Minecraft third-party authentication kernel with full Yggdrasil protocol support and authlib-injector compatibility.

### Release Notes

**Authentication Server**
- Yggdrasil authenticate / validate / refresh / invalidate / signout
- authlib-injector metadata endpoint and selectedProfile compatibility
- Redis-backed atomic token issuance (Lua scripts)
- Three-layer rate limiting (IP, account x IP, concurrency)

**Session Server**
- join / hasJoined / profile endpoints
- RSA-SHA1 texture signing (authlib-injector compatible)
- Skin texture serving with server-wide synchronized visibility
- Redis player cache for hot-path zero-DB access

**Account System**
- Email registration + unique player name binding
- Login / forgot password / reset password
- Four-tier permission model (NormalPlayer / ServerModerator / ServerOwner / PlatformAdmin)
- Two-level ban system (account + player)
- User enumeration prevention and lockout protection

**Infrastructure**
- MariaDB persistence (6 tables)
- Redis caching layer (tokens, tickets, lockout, permission snapshots)
- OpenTelemetry distributed tracing
- Health check endpoint

### Deployment Checklist

- [ ] MariaDB 10.5+ running with `fantasytown_auth` database
- [ ] Redis 7+ running with password configured
- [ ] `appsettings.json` updated (ConnectionStrings, Redis:Password, Yggdrasil:SkinBaseUrl, SkinDomains)
- [ ] RSA signing key generated at configured path (`Signing:PrivateKeyPath`)
- [ ] Skin texture PNGs placed at `{SkinBaseUrl}/textures/skins/{uuid}.png`
- [ ] Firewall: port 5062 (or configured Kestrel port) open

### Commits

Squash merge from develop.
