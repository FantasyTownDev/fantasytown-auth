namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 邮箱格式校验（注册与管理域邮箱改绑共用同一规则）
/// </summary>
public static class EmailAddress
{
    /// <summary>
    /// 简单邮箱验证：必须包含 @ 且 @ 前后都有内容；长度封顶 100（M2：对齐
    /// users.email VARCHAR(100) 列限，超长输入前置拒绝，不让 1406 冒泡成 500）
    /// </summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Length > 100) return false;
        var atIndex = email.IndexOf('@');
        return atIndex > 0 && atIndex < email.Length - 1 && email.Contains('.');
    }
}
