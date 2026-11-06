// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Domain;
using Xunit;

namespace FantasyTown.Auth.UnitTests;

public class UserPermissionTests
{
    [Theory]
    [InlineData(UserPermission.NormalPlayer, false)]
    [InlineData(UserPermission.ServerModerator, true)]
    [InlineData(UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.PlatformAdmin, true)]
    public void IsModerator_ShouldReturnCorrectValue(UserPermission permission, bool expected)
    {
        var result = permission is UserPermission.ServerModerator
            or UserPermission.ServerOwner
            or UserPermission.PlatformAdmin;

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(UserPermission.NormalPlayer, false)]
    [InlineData(UserPermission.ServerModerator, false)]
    [InlineData(UserPermission.ServerOwner, true)]
    [InlineData(UserPermission.PlatformAdmin, true)]
    public void IsOwner_ShouldReturnCorrectValue(UserPermission permission, bool expected)
    {
        var result = permission is UserPermission.ServerOwner or UserPermission.PlatformAdmin;

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(UserPermission.NormalPlayer, false)]
    [InlineData(UserPermission.ServerModerator, false)]
    [InlineData(UserPermission.ServerOwner, false)]
    [InlineData(UserPermission.PlatformAdmin, true)]
    public void IsPlatform_ShouldReturnCorrectValue(UserPermission permission, bool expected)
    {
        var result = permission == UserPermission.PlatformAdmin;

        Assert.Equal(expected, result);
    }
}
