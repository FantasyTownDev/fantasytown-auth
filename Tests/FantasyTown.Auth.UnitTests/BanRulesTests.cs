using FantasyTown.Auth.Modules.Accounts.Domain;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// BanRules 封禁判定时间谓词测试
/// 覆盖 §5.4 要求的 IsBanEffective 四态
/// </summary>
public class BanRulesTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    #region IsBanEffective

    [Fact]
    public void IsBanEffective_Inactive_ReturnsFalse()
    {
        Assert.False(BanRules.IsBanEffective(isActive: false, bannedUntil: null, BaseTime));
    }

    [Fact]
    public void IsBanEffective_InactiveWithFutureDate_ReturnsFalse()
    {
        Assert.False(BanRules.IsBanEffective(isActive: false, bannedUntil: BaseTime.AddDays(1), BaseTime));
    }

    [Fact]
    public void IsBanEffective_ActivePermanent_ReturnsTrue()
    {
        Assert.True(BanRules.IsBanEffective(isActive: true, bannedUntil: null, BaseTime));
    }

    [Fact]
    public void IsBanEffective_ActiveTemporaryNotExpired_ReturnsTrue()
    {
        Assert.True(BanRules.IsBanEffective(isActive: true, bannedUntil: BaseTime.AddDays(1), BaseTime));
    }

    [Fact]
    public void IsBanEffective_ActiveTemporaryExpired_ReturnsFalse()
    {
        Assert.False(BanRules.IsBanEffective(isActive: true, bannedUntil: BaseTime.AddDays(-1), BaseTime));
    }

    [Fact]
    public void IsBanEffective_ActiveTemporaryExactExpiry_ReturnsFalse()
    {
        // bannedUntil.Value > nowUtc => false when equal
        Assert.False(BanRules.IsBanEffective(isActive: true, bannedUntil: BaseTime, BaseTime));
    }

    [Fact]
    public void IsBanEffective_ActiveTemporaryOneSecondBeforeExpiry_ReturnsTrue()
    {
        Assert.True(BanRules.IsBanEffective(isActive: true, bannedUntil: BaseTime.AddSeconds(1), BaseTime));
    }

    [Fact]
    public void IsBanEffective_ActiveTemporaryOneSecondAfterExpiry_ReturnsFalse()
    {
        Assert.False(BanRules.IsBanEffective(isActive: true, bannedUntil: BaseTime.AddSeconds(-1), BaseTime));
    }

    #endregion

    #region IsBanExpired

    [Fact]
    public void IsBanExpired_Inactive_ReturnsTrue()
    {
        Assert.True(BanRules.IsBanExpired(isActive: false, bannedUntil: null, BaseTime));
    }

    [Fact]
    public void IsBanExpired_ActivePermanent_ReturnsFalse()
    {
        Assert.False(BanRules.IsBanExpired(isActive: true, bannedUntil: null, BaseTime));
    }

    [Fact]
    public void IsBanExpired_ActiveTemporaryNotExpired_ReturnsFalse()
    {
        Assert.False(BanRules.IsBanExpired(isActive: true, bannedUntil: BaseTime.AddDays(1), BaseTime));
    }

    [Fact]
    public void IsBanExpired_ActiveTemporaryExpired_ReturnsTrue()
    {
        Assert.True(BanRules.IsBanExpired(isActive: true, bannedUntil: BaseTime.AddDays(-1), BaseTime));
    }

    [Fact]
    public void IsBanExpired_ActiveTemporaryExactExpiry_ReturnsTrue()
    {
        // bannedUntil.Value <= nowUtc => true when equal
        Assert.True(BanRules.IsBanExpired(isActive: true, bannedUntil: BaseTime, BaseTime));
    }

    #endregion

    #region GetBanRemainingSeconds

    [Fact]
    public void GetBanRemainingSeconds_PermanentBan_ReturnsNull()
    {
        Assert.Null(BanRules.GetBanRemainingSeconds(bannedUntil: null, BaseTime));
    }

    [Fact]
    public void GetBanRemainingSeconds_NotExpired_ReturnsPositiveSeconds()
    {
        var remaining = BanRules.GetBanRemainingSeconds(BaseTime.AddHours(1), BaseTime);
        Assert.NotNull(remaining);
        Assert.InRange(remaining.Value, 3599, 3601);
    }

    [Fact]
    public void GetBanRemainingSeconds_Expired_ReturnsZero()
    {
        var remaining = BanRules.GetBanRemainingSeconds(BaseTime.AddHours(-1), BaseTime);
        Assert.NotNull(remaining);
        Assert.Equal(0, remaining.Value);
    }

    [Fact]
    public void GetBanRemainingSeconds_ExactExpiry_ReturnsZero()
    {
        var remaining = BanRules.GetBanRemainingSeconds(BaseTime, BaseTime);
        Assert.NotNull(remaining);
        Assert.Equal(0, remaining.Value);
    }

    [Fact]
    public void GetBanRemainingSeconds_OneSecondBeforeExpiry_ReturnsOneSecond()
    {
        var remaining = BanRules.GetBanRemainingSeconds(BaseTime.AddSeconds(1), BaseTime);
        Assert.NotNull(remaining);
        Assert.InRange(remaining.Value, 0.9, 1.1);
    }

    #endregion
}
