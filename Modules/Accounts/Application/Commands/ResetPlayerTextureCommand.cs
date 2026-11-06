// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Accounts.Application.Commands;

/// <summary>
/// 玩家重置皮肤命令（V5 §7.3 POST /admin/players/{pid}/reset-texture，moderator 策略）
/// </summary>
public sealed record ResetPlayerTextureCommand
{
    /// <summary>操作者 UID</summary>
    public required int ActorUid { get; init; }

    /// <summary>目标玩家 PID</summary>
    public required int Pid { get; init; }
}

/// <summary>
/// 玩家重置皮肤命令结果（HadSkin 反映清除前是否佩戴皮肤）
/// </summary>
public sealed record ResetPlayerTextureResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HadSkin { get; init; }

    public static ResetPlayerTextureResult Success(bool hadSkin) => new()
    {
        IsSuccess = true,
        HadSkin = hadSkin
    };

    public static ResetPlayerTextureResult Failure(string error) => new()
    {
        IsSuccess = false,
        ErrorMessage = error
    };
}
