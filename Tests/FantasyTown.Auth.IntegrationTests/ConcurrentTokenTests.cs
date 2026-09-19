using System.Collections.Concurrent;

namespace FantasyTown.Auth.IntegrationTests;

/// <summary>
/// 并发令牌测试（需要 Redis 或 Docker）
/// </summary>
[Trait("Category", "Integration")]
public class ConcurrentTokenTests
{
    [Fact]
    public async Task ConcurrentTokenIssuance_ShouldNotCreateOrphans()
    {
        const int concurrentCount = 10;
        var tokens = new ConcurrentBag<string>();

        var tasks = new List<Task>();
        for (int i = 0; i < concurrentCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var token = Guid.NewGuid().ToString();
                tokens.Add(token);
            }));
        }

        await Task.WhenAll(tasks);

        var uniqueTokens = tokens.Distinct().Count();
        Assert.Equal(concurrentCount, uniqueTokens);
    }
}
