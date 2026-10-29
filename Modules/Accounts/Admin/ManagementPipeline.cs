// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using FantasyTown.Auth.Modules.Accounts.Application;
using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Persistence;

namespace FantasyTown.Auth.Modules.Accounts.Admin;

/// <summary>
/// 管线闸门请求（V5 §7.1 原则 2 的 HTTP 层表达）。
/// </summary>
public sealed record ManagementGateRequest
{
    /// <summary>操作者 UID（来自可信 uid claim）</summary>
    public required int ActorUid { get; init; }

    /// <summary>目标用户 UID（来自路由；玩家档位下不使用）</summary>
    public required int TargetUid { get; init; }

    /// <summary>目标玩家 PID（来自路由；仅玩家档位使用，§7.3 路由表 "pid"）</summary>
    public int? TargetPid { get; init; }

    /// <summary>操作标识（越权拒绝审计 context，如 "permission-change"）</summary>
    public required string Operation { get; init; }

    /// <summary>越权矩阵档位</summary>
    public required ManagementMatrix Matrix { get; init; }

    /// <summary>★敏感操作：需 actor 当前密码二次确认（V4 评审 R9）</summary>
    public bool Sensitive { get; init; }

    /// <summary>★表单携带的 actor 当前密码；敏感操作缺失按确认失败处理</summary>
    public string? ActorCurrentPassword { get; init; }
}

/// <summary>
/// 管线闸门结果：IsAuthorized=false 时 DenialReason/StatusCode 供 HTTP 层直映射。
/// </summary>
public sealed record ManagementPipelineResult
{
    public bool IsAuthorized { get; init; }
    public string? DenialReason { get; init; }
    public int StatusCode { get; init; }

    public static ManagementPipelineResult Authorized() => new()
    {
        IsAuthorized = true,
        StatusCode = 200
    };

    public static ManagementPipelineResult Denied(string reason) => new()
    {
        IsAuthorized = false,
        DenialReason = reason,
        StatusCode = 403
    };

    public static ManagementPipelineResult NotFound(string reason) => new()
    {
        IsAuthorized = false,
        DenialReason = reason,
        StatusCode = 404
    };
}

/// <summary>
/// 管理管线（V5 §7.1 冻结顺序：Antiforgery → 策略 → 越权矩阵 → 二次确认 → 领域命令）。
/// 阶段一/二由框架承担（Razor Pages 内置 Antiforgery + §5.2 快照策略）；
/// 本服务实现阶段三（越权矩阵，分派 Application 层闸门）与阶段四（★二次确认）；
/// 阶段五（领域命令）在闸门放行后由页面调用 Step 2 handlers——handler 内既有闸门为纵深防御（§7.1 原则 3）。
/// 冻结约束：越权矩阵先于二次确认（防错 #12——注定 403 的请求不消耗验密 CPU）；
/// 两失败分支同文案 403（§7.1 原则 2——无信息泄露）；确认失败计入 actor 的 FAIL_EP 并审计 admin-denied。
/// </summary>
public sealed class ManagementPipeline
{
    /// <summary>越权与确认失败的统一文案（同文案 403，无信息泄露）</summary>
    public const string DenialMessage = "无权操作";

    private readonly AuthDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly ILockoutService _lockouts;
    private readonly IClientIp _clientIp;

    public ManagementPipeline(
        AuthDbContext db,
        IPasswordService passwords,
        ILockoutService lockouts,
        IClientIp clientIp)
    {
        _db = db;
        _passwords = passwords;
        _lockouts = lockouts;
        _clientIp = clientIp;
    }

    public async Task<ManagementPipelineResult> EvaluateAsync(
        ManagementGateRequest request,
        CancellationToken cancellationToken = default)
    {
        // 阶段三：越权矩阵（先判"能否对该目标操作"，拒绝时闸门已落 admin-denied 审计）
        string? denial;
        Domain.User? actor;
        switch (request.Matrix)
        {
            case ManagementMatrix.OwnerUser:
            case ManagementMatrix.ClearLockout:
                {
                    var evaluation = request.Matrix == ManagementMatrix.OwnerUser
                        ? await AdminAuthorization.EvaluateAsync(
                            _db, request.ActorUid, request.TargetUid, request.Operation, cancellationToken)
                        : await ClearLockoutAuthorization.EvaluateAsync(
                            _db, request.ActorUid, request.TargetUid, request.Operation, cancellationToken);
                    denial = evaluation.Denial;
                    actor = evaluation.Actor;
                    break;
                }
            case ManagementMatrix.PlayerModerator:
                {
                    var evaluation = await PlayerAuthorization.EvaluateModeratorTierAsync(
                        _db, request.ActorUid, request.TargetPid ?? 0, request.Operation, cancellationToken);
                    denial = evaluation.Denial;
                    actor = evaluation.Actor;
                    break;
                }
            case ManagementMatrix.PlayerOwner:
                {
                    var evaluation = await PlayerAuthorization.EvaluateOwnerTierAsync(
                        _db, request.ActorUid, request.TargetPid ?? 0, request.Operation, cancellationToken);
                    denial = evaluation.Denial;
                    actor = evaluation.Actor;
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.Matrix, "未实现的管理矩阵档位");
        }

        if (denial != null)
        {
            return denial is "用户不存在" or "玩家不存在"
                ? ManagementPipelineResult.NotFound(denial)
                : ManagementPipelineResult.Denied(denial);
        }

        // 阶段四：★二次确认（actor 当前密码，恒定时间比对；失败计入 FAIL_EP + admin-denied 审计）
        if (request.Sensitive)
        {
            var confirmed = actor != null
                && await _passwords.VerifyPasswordAsync(request.ActorCurrentPassword ?? "", actor.Password, cancellationToken);

            if (!confirmed)
            {
                if (actor != null)
                {
                    await _lockouts.RecordFailureAsync(
                        actor.Uid, actor.Email, _clientIp.GetClientIp() ?? "unknown", cancellationToken);
                }

                _db.AuthLogs.Add(new AuthLog
                {
                    Action = "admin-denied",
                    ActorId = request.ActorUid,
                    SubjectId = request.TargetUid,
                    Context = $"{request.Operation}:confirmation",
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);

                return ManagementPipelineResult.Denied(DenialMessage);
            }
        }

        return ManagementPipelineResult.Authorized();
    }
}
