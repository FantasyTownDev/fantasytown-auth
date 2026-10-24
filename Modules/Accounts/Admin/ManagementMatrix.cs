namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// 管理管线越权矩阵档位（V5 §7.2）：
/// 各档位的白名单/CanManage/NotSelf 判定收敛于 Application 层闸门（唯一实现点），
/// 管线仅按档位分派。Step 4 玩家域接入时在此扩展档位并补对应闸门。
/// </summary>
public enum ManagementMatrix
{
    /// <summary>owner 级用户域操作（任命/撤销协管、封禁/解封、强制重置、邮箱改绑、软删）</summary>
    OwnerUser,

    /// <summary>解除 LOCKOUT（moderator 策略；自我解锁放行，V5 评审 N1）</summary>
    ClearLockout
}
