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
