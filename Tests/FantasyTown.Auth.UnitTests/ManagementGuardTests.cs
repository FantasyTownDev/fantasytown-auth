// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// ManagementGuard 越权规则矩阵（V5 §7.2 冻结语义）
/// 规则一：CanManage 严格大于（全系统唯一允许比较权限大小之处）
/// 规则二：NotSelf 禁止自我操作
/// 唯一例外：CanClearLockout 自我解锁放行（V5 评审 N1）
/// </summary>
public class ManagementGuardTests
{
    // ---------- CanManage：actor × target 全矩阵（16 组合全覆盖） ----------

    public static TheoryData<UserPermission, UserPermission, bool> CanManageMatrix => new()
    {
        // 同级不可互操作
        { UserPermission.NormalPlayer, UserPermission.NormalPlayer, false },
        { UserPermission.ServerModerator, UserPermission.ServerModerator, false },
        { UserPermission.ServerOwner, UserPermission.ServerOwner, false },
        { UserPermission.PlatformAdmin, UserPermission.PlatformAdmin, false },
        // 向上操作禁止
        { UserPermission.NormalPlayer, UserPermission.ServerModerator, false },
        { UserPermission.NormalPlayer, UserPermission.ServerOwner, false },
        { UserPermission.NormalPlayer, UserPermission.PlatformAdmin, false },
        { UserPermission.ServerModerator, UserPermission.ServerOwner, false },
        { UserPermission.ServerModerator, UserPermission.PlatformAdmin, false },
        { UserPermission.ServerOwner, UserPermission.PlatformAdmin, false },
        // 向下管理放行
        { UserPermission.ServerModerator, UserPermission.NormalPlayer, true },
        { UserPermission.ServerOwner, UserPermission.NormalPlayer, true },
        { UserPermission.ServerOwner, UserPermission.ServerModerator, true },
        { UserPermission.PlatformAdmin, UserPermission.NormalPlayer, true },
        { UserPermission.PlatformAdmin, UserPermission.ServerModerator, true },
        { UserPermission.PlatformAdmin, UserPermission.ServerOwner, true },
    };

    [Theory]
    [MemberData(nameof(CanManageMatrix))]
    public void CanManage_MatrixMatchesFrozenSemantics(UserPermission actor, UserPermission target, bool expected)
        => Assert.Equal(expected, ManagementGuard.CanManage(actor, target));

    [Fact]
    public void Invariant_ModeratorCanManageOnlyNormalPlayers()
    {
        Assert.True(ManagementGuard.CanManage(UserPermission.ServerModerator, UserPermission.NormalPlayer));
        Assert.False(ManagementGuard.CanManage(UserPermission.ServerModerator, UserPermission.ServerModerator));
        Assert.False(ManagementGuard.CanManage(UserPermission.ServerModerator, UserPermission.ServerOwner));
    }

    [Fact]
    public void Invariant_PlatformAdminHasFullManagementReach()
    {
        Assert.True(ManagementGuard.CanManage(UserPermission.PlatformAdmin, UserPermission.NormalPlayer));
        Assert.True(ManagementGuard.CanManage(UserPermission.PlatformAdmin, UserPermission.ServerModerator));
        Assert.True(ManagementGuard.CanManage(UserPermission.PlatformAdmin, UserPermission.ServerOwner));
    }

    // ---------- NotSelf ----------

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(7, 7, false)]
    [InlineData(0, int.MaxValue, true)]
    public void NotSelf_ComparesUid(int actorUid, int targetUid, bool expected)
        => Assert.Equal(expected, ManagementGuard.NotSelf(actorUid, targetUid));

    // ---------- CanClearLockout（V5 N1 专用判定） ----------

    /// <summary>
    /// 锚定用例（V5 §11.1 / P4 验收门）：唯一服主被锁后的自救通道
    /// </summary>
    [Fact]
    public void CanClearLockout_OwnerClearingOwnLockout_IsAllowed()
        => Assert.True(ManagementGuard.CanClearLockout(
            UserPermission.ServerOwner, UserPermission.ServerOwner, 42, 42));

    [Theory]
    // 自我解锁：非降权类操作，任何级别自我放行（moderator 策略闸门由管理管线承担）
    [InlineData(UserPermission.NormalPlayer, UserPermission.ServerOwner, 1, 1, true)]
    [InlineData(UserPermission.ServerModerator, UserPermission.ServerModerator, 5, 5, true)]
    // 他人：操作者权限严格高于目标（协管不能解服主的锁）
    [InlineData(UserPermission.ServerModerator, UserPermission.NormalPlayer, 1, 2, true)]
    [InlineData(UserPermission.ServerModerator, UserPermission.ServerOwner, 1, 2, false)]
    [InlineData(UserPermission.ServerModerator, UserPermission.ServerModerator, 1, 2, false)]
    [InlineData(UserPermission.ServerOwner, UserPermission.ServerModerator, 9, 5, true)]
    [InlineData(UserPermission.ServerOwner, UserPermission.ServerOwner, 9, 5, false)]
    [InlineData(UserPermission.PlatformAdmin, UserPermission.ServerOwner, 3, 8, true)]
    public void CanClearLockout_Matrix(UserPermission actor, UserPermission target, int actorUid, int targetUid, bool expected)
        => Assert.Equal(expected, ManagementGuard.CanClearLockout(actor, target, actorUid, targetUid));

    // ---------- 组合不变量：非自我豁免仅限 CanClearLockout ----------

    [Fact]
    public void RegularOperation_SelfIsAlwaysDenied()
    {
        // 常规操作需 CanManage 且 NotSelf 同时满足：自我操作永不放行
        Assert.False(
            ManagementGuard.CanManage(UserPermission.ServerOwner, UserPermission.NormalPlayer)
            && ManagementGuard.NotSelf(10, 10));
    }

    [Fact]
    public void SelfClearLockout_DoesNotGrantOtherOperations()
    {
        // 豁免只存在于 CanClearLockout：同参数走常规判定仍拒绝
        const int uid = 10;
        Assert.True(ManagementGuard.CanClearLockout(UserPermission.ServerOwner, UserPermission.ServerOwner, uid, uid));
        Assert.False(ManagementGuard.NotSelf(uid, uid));
    }
}
