namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 封禁状态判定 - 时间谓词
/// </summary>
public static class BanRules
{
    /// <summary>
    /// 判断封禁是否生效
    /// </summary>
    /// <param name="isActive">封禁记录是否激活</param>
    /// <param name="bannedUntil">封禁截止时间（null 表示永久封禁）</param>
    /// <param name="nowUtc">当前 UTC 时间</param>
    /// <returns>true 表示封禁生效中</returns>
    public static bool IsBanEffective(bool isActive, DateTime? bannedUntil, DateTime nowUtc)
    {
        // 未激活的封禁记录不生效
        if (!isActive) return false;
        
        // 激活且无截止时间 = 永久封禁
        if (bannedUntil is null) return true;
        
        // 激活且截止时间在未来 = 临时封禁未到期
        return bannedUntil.Value > nowUtc;
    }

    /// <summary>
    /// 判断封禁是否已过期
    /// </summary>
    public static bool IsBanExpired(bool isActive, DateTime? bannedUntil, DateTime nowUtc)
    {
        if (!isActive) return true;
        if (bannedUntil is null) return false; // 永久封禁不过期
        return bannedUntil.Value <= nowUtc;
    }

    /// <summary>
    /// 获取封禁剩余时间（秒）
    /// </summary>
    public static double? GetBanRemainingSeconds(DateTime? bannedUntil, DateTime nowUtc)
    {
        if (bannedUntil is null) return null; // 永久封禁
        var remaining = (bannedUntil.Value - nowUtc).TotalSeconds;
        return remaining > 0 ? remaining : 0;
    }
}
