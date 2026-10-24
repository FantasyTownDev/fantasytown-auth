namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 邮箱格式校验（注册与管理域邮箱改绑共用同一规则）
/// </summary>
public static class EmailAddress
{
    /// <summary>
    /// 简单邮箱验证：必须包含 @ 且 @ 前后都有内容
    /// </summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var atIndex = email.IndexOf('@');
        return atIndex > 0 && atIndex < email.Length - 1 && email.Contains('.');
    }
}
