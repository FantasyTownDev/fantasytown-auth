using FantasyTown.Auth.Modules.Shared;

namespace FantasyTown.Auth.UnitTests;

/// <summary>
/// IPlayerCacheInvalidation 替身：记录失效调用（验证管理命令 H5 副作用接线）。
/// </summary>
internal sealed class RecordingPlayerCacheInvalidation : IPlayerCacheInvalidation
{
    public List<string> InvalidatedUuids { get; } = new();

    public ValueTask InvalidatePlayerAsync(string uuid, CancellationToken cancellationToken = default)
    {
        InvalidatedUuids.Add(uuid);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// ITokenRevocation 替身：记录吊销调用（验证管理命令 H5 副作用接线）。
/// </summary>
internal sealed class RecordingTokenRevocation : ITokenRevocation
{
    public List<string> RevokedEmails { get; } = new();

    public ValueTask RevokeAllAsync(string email, CancellationToken cancellationToken = default)
    {
        RevokedEmails.Add(email);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// IBanSignalPublisher 替身：记录封禁信号（验证封禁/解封命令的 BAN_SIGNAL 广播接线）。
/// </summary>
internal sealed class RecordingBanSignalPublisher : IBanSignalPublisher
{
    public List<BanSignal> Published { get; } = new();

    public ValueTask PublishAsync(BanSignal signal, CancellationToken cancellationToken = default)
    {
        Published.Add(signal);
        return ValueTask.CompletedTask;
    }
}
