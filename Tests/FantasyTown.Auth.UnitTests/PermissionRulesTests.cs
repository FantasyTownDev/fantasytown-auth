// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.UnitTests;

public class PermissionRulesTests
{
    [Theory]
    [InlineData(UserPermission.ServerModerator, true)]
    [InlineData(UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.PlatformAdmin, true)]
    [InlineData(UserPermission.NormalPlayer, false)]
    public void IsModerator_ReturnsExpectedResult(UserPermission role, bool expected)
    {
        Assert.Equal(expected, PermissionRules.IsModerator(role));
    }

    [Theory]
    [InlineData(UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.ServerModerator, false)]
    [InlineData(UserPermission.PlatformAdmin, false)]
    [InlineData(UserPermission.NormalPlayer, false)]
    public void IsOwner_ReturnsExpectedResult(UserPermission role, bool expected)
    {
        Assert.Equal(expected, PermissionRules.IsOwner(role));
    }

    [Theory]
    [InlineData(UserPermission.PlatformAdmin, true)]
    [InlineData(UserPermission.ServerOwner, false)]
    [InlineData(UserPermission.ServerModerator, false)]
    [InlineData(UserPermission.NormalPlayer, false)]
    public void IsPlatformAdmin_ReturnsExpectedResult(UserPermission role, bool expected)
    {
        Assert.Equal(expected, PermissionRules.IsPlatformAdmin(role));
    }

    [Theory]
    [InlineData(UserPermission.NormalPlayer, true)]
    [InlineData(UserPermission.ServerModerator, false)]
    [InlineData(UserPermission.ServerOwner, false)]
    [InlineData(UserPermission.PlatformAdmin, false)]
    public void IsNormalPlayer_ReturnsExpectedResult(UserPermission role, bool expected)
    {
        Assert.Equal(expected, PermissionRules.IsNormalPlayer(role));
    }

    [Theory]
    [InlineData(UserPermission.PlatformAdmin, UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.PlatformAdmin, UserPermission.ServerModerator, true)]
    [InlineData(UserPermission.PlatformAdmin, UserPermission.NormalPlayer, true)]
    [InlineData(UserPermission.ServerOwner, UserPermission.ServerModerator, true)]
    [InlineData(UserPermission.ServerOwner, UserPermission.NormalPlayer, true)]
    [InlineData(UserPermission.ServerOwner, UserPermission.PlatformAdmin, false)]
    [InlineData(UserPermission.ServerModerator, UserPermission.NormalPlayer, true)]
    [InlineData(UserPermission.ServerModerator, UserPermission.ServerOwner, false)]
    [InlineData(UserPermission.NormalPlayer, UserPermission.NormalPlayer, false)]
    public void CanManage_ReturnsExpectedResult(UserPermission actor, UserPermission target, bool expected)
    {
        Assert.Equal(expected, PermissionRules.CanManage(actor, target));
    }

    [Theory]
    [InlineData(UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.PlatformAdmin, true)]
    [InlineData(UserPermission.ServerModerator, false)]
    [InlineData(UserPermission.NormalPlayer, false)]
    public void CanAppointModerator_ReturnsExpectedResult(UserPermission role, bool expected)
    {
        Assert.Equal(expected, PermissionRules.CanAppointModerator(role));
    }
}
