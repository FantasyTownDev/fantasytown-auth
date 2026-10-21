namespace FantasyTown.Auth.Modules.Accounts.Domain;

/// <summary>
/// 角色名规范（现行规则与注册路径一致：3–16 位字母/数字/下划线）。
/// SiteOptions.PlayerNameRule/PlayerNameLength 的 official/cjk/utf8 分级规则引擎
/// 尚未实现（配置项预留），实现后此处为唯一收敛点。
/// </summary>
public static class PlayerNameRules
{
    public static bool IsValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length < 3 || name.Length > 16) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
