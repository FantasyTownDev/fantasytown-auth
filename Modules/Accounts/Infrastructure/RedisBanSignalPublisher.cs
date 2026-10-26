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
