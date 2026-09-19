namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 时钟抽象，支持确定性测试。
/// 注入到需要当前 UTC 时间的位置（如 IsBanEffective 检查）。
/// </summary>
public interface IClock
{
    /// <summary>
    /// 获取当前 UTC 日期和时间。
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// 使用系统时钟的默认实现。
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
