using System.Collections.Concurrent;

namespace FantasyTown.Auth.Modules.Yggdrasil.Authserver;

/// <summary>
/// 内存令牌服务实现（用于单元测试）
/// </summary>
public sealed class InMemoryTokenService : ITokenService
{
    private readonly ConcurrentDictionary<string, TokenRecord> _tokens = new();
    private readonly ConcurrentDictionary<string, string> _emailToAccessToken = new();
    private readonly TimeSpan _expire1 = TimeSpan.FromDays(3);
    private readonly TimeSpan _expire2 = TimeSpan.FromDays(7);

    public ValueTask<TokenRecord> IssueAsync(int uid, string email, string? clientToken, string? profileId, byte role, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        var tokenValue = accessToken ?? Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var token = new TokenRecord
        {
            OwnerUid = uid,
            ClientToken = clientToken,
            AccessToken = tokenValue,
            ProfileId = profileId,
            CreatedAt = now,
            Role = role
        };

        // 吊销旧令牌
        if (_emailToAccessToken.TryRemove(email, out var oldAccessToken))
        {
            _tokens.TryRemove(oldAccessToken, out _);
        }

        _tokens[tokenValue] = token;
        _emailToAccessToken[email] = tokenValue;

        return ValueTask.FromResult(token);
    }

    public ValueTask<TokenRecord?> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (_tokens.TryGetValue(accessToken, out var token))
        {
            // 检查 expire_1 窗口
            if (DateTimeOffset.UtcNow - token.CreatedAt <= _expire1)
            {
                return ValueTask.FromResult<TokenRecord?>(token);
            }
        }

        return ValueTask.FromResult<TokenRecord?>(null);
    }

    public ValueTask<TokenRecord?> RefreshAsync(string accessToken, string? clientToken, string? newProfileId, CancellationToken cancellationToken = default)
    {
        if (_tokens.TryGetValue(accessToken, out var oldToken))
        {
            // 检查 expire_2 窗口
            if (DateTimeOffset.UtcNow - oldToken.CreatedAt > _expire2)
            {
                return ValueTask.FromResult<TokenRecord?>(null);
            }

            // 检查 clientToken 匹配
            if (clientToken != null && oldToken.ClientToken != null && oldToken.ClientToken != clientToken)
            {
                return ValueTask.FromResult<TokenRecord?>(null);
            }

            // 删除旧令牌
            _tokens.TryRemove(accessToken, out _);

            // 签发新令牌
            var newAccessToken = Guid.NewGuid().ToString("N");
            var newToken = new TokenRecord
            {
                OwnerUid = oldToken.OwnerUid,
                ClientToken = clientToken ?? oldToken.ClientToken,
                AccessToken = newAccessToken,
                ProfileId = newProfileId ?? oldToken.ProfileId,
                CreatedAt = DateTimeOffset.UtcNow,
                Role = oldToken.Role
            };

            _tokens[newAccessToken] = newToken;

            // 更新 email → accessToken 映射
            foreach (var kvp in _emailToAccessToken)
            {
                if (kvp.Value == accessToken)
                {
                    _emailToAccessToken[kvp.Key] = newAccessToken;
                    break;
                }
            }

            return ValueTask.FromResult<TokenRecord?>(newToken);
        }

        return ValueTask.FromResult<TokenRecord?>(null);
    }

    public ValueTask RevokeAllAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_emailToAccessToken.TryRemove(email, out var accessToken))
        {
            _tokens.TryRemove(accessToken, out _);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeByTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        _tokens.TryRemove(accessToken, out _);
        return ValueTask.CompletedTask;
    }
}
