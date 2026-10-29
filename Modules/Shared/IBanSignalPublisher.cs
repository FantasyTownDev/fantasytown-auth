// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 封禁/解封信号量（V5 评审拍板：Redis pub/sub 广播，供未来 Java Agent / 服务器插件
/// 消费实现游戏内踢出与登录提示）。
/// 失败不吞——与 H5 缓存失效/吊销副作用同语义，Redis 故障必须可见，
/// 静默跳过即破坏封禁即时性。
/// </summary>
public interface IBanSignalPublisher
{
    ValueTask PublishAsync(BanSignal signal, CancellationToken cancellationToken = default);
}

/// <summary>
/// 封禁信号事件。序列化为 camelCase JSON、null 字段忽略（非 .NET 消费方友好），
/// 字段语义见各属性注释；未列出的管理动作不广播。
/// </summary>
public sealed record BanSignal
{
    /// <summary>pub/sub 频道名（V5 键空间表补录行：BAN_SIGNAL）。</summary>
    public const string ChannelName = "BAN_SIGNAL";

    /// <summary>动作："ban" | "unban"。</summary>
    public required string Action { get; init; }

    /// <summary>范围："user"（账号封禁）| "player"（玩家封禁）。</summary>
    public required string Scope { get; init; }

    /// <summary>账号主键（scope=user 必填，scope=player 为属主 uid）。</summary>
    public int? Uid { get; init; }

    /// <summary>玩家主键（scope=player 必填）。</summary>
    public int? Pid { get; init; }

    /// <summary>玩家 UUID（scope=player；游戏侧踢出定位键）。</summary>
    public string? Uuid { get; init; }

    /// <summary>玩家名（scope=player；日志与展示用）。</summary>
    public string? PlayerName { get; init; }

    /// <summary>账号邮箱（scope=user 必填；scope=player 为属主邮箱）。</summary>
    public string? Email { get; init; }

    /// <summary>封禁理由（ban 时可为 null；unban 不携带）。</summary>
    public string? Reason { get; init; }

    /// <summary>封禁截止时间（ISO 8601；null = 永久封禁）。</summary>
    public DateTime? Until { get; init; }

    /// <summary>操作者账号主键（审计对齐）。</summary>
    public required int OperatorUid { get; init; }

    /// <summary>事件发生时间（UTC）。</summary>
    public required DateTime At { get; init; }
}

/// <summary>
/// 无操作回落：DI 未注册时（测试直接 new Handler）不广播，避免空引用。
/// </summary>
public sealed class NoopBanSignalPublisher : IBanSignalPublisher
{
    public static readonly NoopBanSignalPublisher Instance = new();

    private NoopBanSignalPublisher()
    {
    }

    public ValueTask PublishAsync(BanSignal signal, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
