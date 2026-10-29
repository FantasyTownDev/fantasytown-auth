// Copyright (c) 2026 FantasyTownDev. All rights reserved.
// SPDX-License-Identifier: AGPL-3.0-only
// SPDX-NoticeRef: NOTICE.md
// 本文件为 FantasyTown-Auth 项目新增材料，适用 LICENSE 与 NOTICE.md 之 AGPLv3 §7 附加条款。
// 算法/校验/令牌签发/权重类逻辑为固定实现；任何改动将触发 AGPLv3 §13 对应源码义务。

using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTown.Auth.Modules.Shared;
using StackExchange.Redis;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// Redis pub/sub 封禁信号发布（BAN_SIGNAL 频道，V5 键空间表补录）。
/// camelCase + 忽略 null，供未来 Java Agent / 插件直接解析；
/// 发布失败异常外抛——与 H5 缓存失效/吊销的故障可见性语义一致，不吞错。
/// </summary>
public sealed class RedisBanSignalPublisher : IBanSignalPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConnectionMultiplexer _redis;

    public RedisBanSignalPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async ValueTask PublishAsync(BanSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = JsonSerializer.Serialize(signal, JsonOptions);
        await _redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(BanSignal.ChannelName),
            message);
    }
}
