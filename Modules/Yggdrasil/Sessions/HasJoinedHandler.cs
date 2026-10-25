using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil hasJoined 处理器
/// 流程：GETDEL 票据 → PLAYER 缓存回源 → 签名 → 返回 profile
/// </summary>
public sealed class HasJoinedHandler
{
    private readonly IPlayerDirectory _directory;
    private readonly ITicketService _ticketService;
    private readonly IPlayerCache _playerCache;
    private readonly ISigningService _signingService;
    private readonly YggOptions _yggOptions;

    public HasJoinedHandler(
        IPlayerDirectory directory,
        ITicketService ticketService,
        IPlayerCache playerCache,
        ISigningService signingService,
        YggOptions yggOptions)
    {
        _directory = directory;
        _ticketService = ticketService;
        _playerCache = playerCache;
        _signingService = signingService;
        _yggOptions = yggOptions;
    }

    public async Task<HasJoinedResult> HandleAsync(string username, string serverId, CancellationToken cancellationToken = default)
    {
        // 1. 消费票据（GETDEL，一次性）
        var uuid = await _ticketService.ConsumeAsync(serverId, cancellationToken);
        if (uuid == null)
        {
            return HasJoinedResult.NotFound();
        }

        // 2. 尝试从缓存获取玩家状态
        var snapshot = await _playerCache.GetAsync(uuid, cancellationToken);
        if (snapshot == null)
        {
            // 缓存未命中，回源 DB
            snapshot = await LoadFromDbAsync(uuid, cancellationToken);
            if (snapshot == null)
            {
                return HasJoinedResult.NotFound();
            }

            // 写入缓存
            await _playerCache.SetAsync(uuid, snapshot, cancellationToken);
        }

        // 3. 检查封禁状态（复用冻结时间谓词 BanRules.IsBanEffective，T3-016）
        if (BanRules.IsBanEffective(snapshot.IsBanned, snapshot.BannedUntil, DateTime.UtcNow))
        {
            return HasJoinedResult.NotFound();
        }

        // 4. 生成签名 profile
        var profile = GenerateSignedProfile(uuid, snapshot);

        return HasJoinedResult.Success(profile);
    }

    private async Task<PlayerSnapshot?> LoadFromDbAsync(string uuid, CancellationToken cancellationToken)
    {
        // 回源经 IPlayerDirectory（H5 模块纪律边界；内部回源 SQL：players LEFT JOIN 当前 active player_bans）
        var player = await _directory.FindByUuidAsync(uuid, cancellationToken);
        if (player == null)
        {
            return null;
        }

        return new PlayerSnapshot
        {
            Name = player.Name,
            LastModified = player.LastModified.ToUnixTimeMilliseconds(),
            IsBanned = player.IsBanned,
            BannedUntil = player.BannedUntil?.UtcDateTime,
            TexturesUrl = $"{_yggOptions.SkinBaseUrl}/textures/skins/{uuid}.png",
            TexturesSignature = null
        };
    }

    private SignedProfile GenerateSignedProfile(string uuid, PlayerSnapshot snapshot)
    {
        // 构建纹理载荷
        var texturePayload = new TexturePayload
        {
            Timestamp = snapshot.LastModified,
            ProfileId = uuid,
            ProfileName = snapshot.Name,
            Textures = new TextureData
            {
                Skin = snapshot.TexturesUrl != null
                    ? new SkinData { Url = snapshot.TexturesUrl }
                    : null
            }
        };

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(texturePayload);
        var valueBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson));

        // authlib-injector 签名格式：SHA1withRSA( value UTF-8 )
        // 验证逻辑在 YggdrasilKeyTransformUnit.verifyPropertySignature()
        // 仅签名 propertyValue.getBytes()，不含 name
        var signature = _signingService.Sign(valueBase64);

        var properties = new List<SignedProperty>
        {
            new()
            {
                Name = "textures",
                Value = valueBase64,
                Signature = signature
            }
        };

        return new SignedProfile
        {
            Uuid = uuid,
            Name = snapshot.Name,
            Properties = properties
        };
    }
}

public sealed record HasJoinedResult
{
    public bool IsValid { get; init; }
    public SignedProfile? Profile { get; init; }

    public static HasJoinedResult Success(SignedProfile profile) => new() { IsValid = true, Profile = profile };
    public static HasJoinedResult NotFound() => new() { IsValid = false };
}
