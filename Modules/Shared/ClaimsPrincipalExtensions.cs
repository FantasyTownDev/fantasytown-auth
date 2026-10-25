using System.Security.Claims;

namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// ClaimsPrincipal 读取扩展。
/// uid claim 由 CookieAuthService 签发（DataProtection 保护、不可伪造），
/// 是唯一可信的主体标识——角色/封禁一律不读 Cookie claim，改读 PERM 快照（V5 §5.2，防错 #1）。
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// 读取可信 uid claim；缺失或非数字返回 null（调用方按未认证处理）。
    /// </summary>
    public static int? GetUid(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("uid")?.Value, out var uid) ? uid : null;
}
