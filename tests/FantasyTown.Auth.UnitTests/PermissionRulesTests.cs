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
