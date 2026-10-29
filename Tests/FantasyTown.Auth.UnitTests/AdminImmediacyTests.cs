// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Security.Claims;
using FantasyTown.Auth.Middleware;
using FantasyTown.Auth.Modules.Accounts.Application.Commands;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// V5 §11.1 Web 即时性三场景（撤权/封禁/软删）——管理命令与会话拦截中间件的端到端联动回归。
/// 既有测试的缺口：handler 测试只断言 DEL PERM（快照→null），middleware 测试只断言孤立 302，
/// 二者从未联动。真正的 SLA 是「管理命令落库 + DEL PERM 之后，**同一快照**喂给
/// RejectBannedUserMiddleware 的下一请求即被拒」。此处把命令副作用与中间件判定串成一条链：
///   撤权 → 快照清除 → 中间件读 null → 哨兵语义 302（非封禁文案，V5 N2）
///   封禁 → DEL PERM → 中间件回源重建 → IsBanEffective 生效 → 302（banned=true）
///   软删 → DEL PERM → 回源经全局过滤器排除 → 空 → 哨兵 302（会话即时失效，同 ban）
/// </summary>
public class AdminImmediacyTests
{
    private static AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private static async Task SeedOwnerAndNormalAsync(AuthDbContext db, int targetUid = 1001)
    {
        db.Users.Add(new User
        {
            Uid = 1,
            Email = "owner@example.com",
            Password = "hashed_owner",
            Permission = UserPermission.ServerOwner,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        db.Users.Add(new User
        {
            Uid = targetUid,
            Email = "user@example.com",
            Password = "hashed_user",
            Permission = UserPermission.NormalPlayer,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();
    }

    private static HttpContext CreateContext(int uid)
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim("uid", uid.ToString()) }, "test");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private static async Task AssertSessionRejectedAsync(IPermissionSnapshot snapshot, int uid, bool expectBannedFlag)
    {
        var context = CreateContext(uid);
        var nextCalled = false;
        var middleware = new RejectBannedUserMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, snapshot);

        Assert.False(nextCalled); // 下一请求未到达业务，直接被会话拦截
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        var location = context.Response.Headers.Location.ToString();
        Assert.Contains("/Account/Login", location);
        if (expectBannedFlag)
            Assert.Contains("banned=true", location);
        else
            Assert.DoesNotContain("banned=true", location); // 哨兵/撤权路径：非封禁文案（V5 N2）
    }

    [Fact]
    public async Task RevokeModerator_SessionRejectedOnNextRequest()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        // 目标就地提权为协管（供撤销）：uid=1001 由 SeedOwnerAndNormalAsync 已种为 NormalPlayer
        var target = await db.Users.FirstAsync(u => u.Uid == 1001);
        target.Permission = UserPermission.ServerModerator;
        await db.SaveChangesAsync();
        var snapshot = new InMemoryPermissionSnapshot();
        await snapshot.SetAsync(1001, new PermissionSnapshot { Permission = UserPermission.ServerModerator });

        var handler = new ChangeUserPermissionHandler(db, snapshot);
        var result = await handler.HandleAsync(new ChangeUserPermissionCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            NewPermission = UserPermission.NormalPlayer // 撤销协管
        });

        Assert.True(result.IsSuccess);
        Assert.Null(await snapshot.GetAsync(1001)); // DEL PERM 已落

        // 同一快照 → 下一请求中间件即时拒绝（哨兵语义，非封禁文案）
        await AssertSessionRejectedAsync(snapshot, uid: 1001, expectBannedFlag: false);
    }

    /// <summary>
    /// 可回源权限快照（镜像生产 RedisPermissionSnapshot 的回源 + N2 哨兵语义，去掉 Redis 层），
    /// 供封禁场景验证「DEL PERM → 回源重建为生效封禁 → 中间件 banned=true 拒绝」这条真实链路。
    /// </summary>
    private sealed class SourceBackedPermissionSnapshot : IPermissionSnapshot
    {
        private readonly AuthDbContext _db;
        private readonly Dictionary<int, PermissionSnapshot> _cache = new();

        public SourceBackedPermissionSnapshot(AuthDbContext db) => _db = db;

        public async Task<PermissionSnapshot?> GetAsync(int uid, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(uid, out var hit))
                return hit;

            var user = await _db.Users
                .Where(u => u.Uid == uid && !u.IsDeleted)
                .Select(u => new PermissionSnapshot
                {
                    Permission = u.Permission,
                    IsBanned = u.IsBanned,
                    BannedUntil = u.BannedUntil,
                    BannedReason = u.BannedReason,
                    CreatedAt = DateTime.UtcNow
                })
                .FirstOrDefaultAsync(cancellationToken);

            // N2 哨兵：回源空结果 = 封禁等效（可缓存值）
            var snapshot = user ?? new PermissionSnapshot
            {
                Permission = UserPermission.NormalPlayer,
                IsBanned = true,
                BannedUntil = null,
                CreatedAt = DateTime.UtcNow
            };
            _cache[uid] = snapshot;
            return snapshot;
        }

        public Task SetAsync(int uid, PermissionSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _cache[uid] = snapshot;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(int uid, CancellationToken cancellationToken = default)
        {
            _cache.Remove(uid);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BanUser_SessionRejectedOnNextRequest()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var snapshot = new SourceBackedPermissionSnapshot(db);
        await snapshot.SetAsync(1001, new PermissionSnapshot { Permission = UserPermission.NormalPlayer });

        var handler = new BanUserHandler(db, snapshot, new RecordingTokenRevocation());
        var result = await handler.HandleAsync(new BanUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1,
            Reason = "immediacy",
            Until = DateTime.UtcNow.AddHours(1)
        });

        Assert.True(result.IsSuccess);

        // DEL PERM 已落（V5 §5.2）：中间件首次读触发回源 → 重建为生效封禁 → banned=true 拒绝
        var rebuilt = await snapshot.GetAsync(1001);
        Assert.NotNull(rebuilt);
        Assert.True(BanRules.IsBanEffective(rebuilt!.IsBanned, rebuilt.BannedUntil, DateTime.UtcNow));

        await AssertSessionRejectedAsync(snapshot, uid: 1001, expectBannedFlag: true);
    }

    [Fact]
    public async Task SoftDelete_SessionRejectedOnNextRequest()
    {
        using var db = CreateInMemoryDbContext();
        await SeedOwnerAndNormalAsync(db);
        var snapshot = new InMemoryPermissionSnapshot();
        await snapshot.SetAsync(1001, new PermissionSnapshot { Permission = UserPermission.NormalPlayer });

        var handler = new SoftDeleteUserHandler(db, snapshot, new RecordingTokenRevocation());
        var result = await handler.HandleAsync(new SoftDeleteUserCommand
        {
            TargetUid = 1001,
            ActorUid = 1
        });

        Assert.True(result.IsSuccess);
        Assert.Null(await snapshot.GetAsync(1001)); // DEL PERM 已落

        // 回源经全局过滤器排除软删户 → 空结果 → 哨兵语义 → 下一请求即时拒绝（同 ban 的即时性，V5 N2）
        await AssertSessionRejectedAsync(snapshot, uid: 1001, expectBannedFlag: false);
    }
}
