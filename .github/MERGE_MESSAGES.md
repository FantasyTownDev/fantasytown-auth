# Merge Commit Messages

## feature/ygg-authenticate -> develop

### Merge Commit (Squash)

```
feat: implement P0-P3 Yggdrasil authentication kernel

Complete Minecraft third-party auth kernel: Yggdrasil authserver +
sessionserver + skin service. authlib-injector compatible.

P0: EF Core/Redis/Razor Pages/OpenTelemetry/health
P1: Accounts (register/login/forgot/reset) + security hardening
P2: Authserver (5 endpoints) + Lua token service + golden fixtures
P3: Sessionserver (join/hasJoined/profile) + RSA-SHA1 signing

203 unit tests, contract tests, production-verified skin signing.
```

### Extend Message

```
Co-authored-by: Xiaomi MiMo <mimo@xiaomi.com>
```

---

## develop -> main

### Merge Commit

```
release: v0.1.0-alpha - Yggdrasil authentication kernel (P0-P3)

First alpha release of FantasyTown.Auth. Minecraft third-party
authentication with full Yggdrasil protocol and authlib-injector
compatibility.
```

### Extend Message

```
BREAKING CHANGE: Initial alpha release. All P0-P3 milestones complete.
See README.md for deployment checklist and configuration.
```
