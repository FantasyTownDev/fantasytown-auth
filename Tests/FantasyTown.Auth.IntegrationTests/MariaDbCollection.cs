namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 所有依赖本地 MariaDB 共享库的集成测试归入同一集合串行执行：
/// 并行类间会竞争库全局状态（如种子提权的"库中无任何 ServerOwner"前置、UNIQUE 占位），
/// 跨类并行曾导致 ConcurrentSeedEmailRegistration 与玩家管理测试的提权判定竞争失败。
/// </summary>
[CollectionDefinition("MariaDB")]
public class MariaDbCollection
{
}
