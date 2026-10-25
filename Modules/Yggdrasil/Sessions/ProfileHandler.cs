using FantasyTown.Auth.Modules.Shared;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil profile/{uuid} 处理器
/// 流程：PLAYER 缓存回源 → 签名 → 返回 profile
/// 支持 ETag=lastModified 输出缓存
/// </summary>
public sealed class ProfileHandler
{
    private readonly IPlayerDirectory _directory;
    private readonly IPlayerCache _playerCache;
    private readonly ISigningService _signingService;
    private readonly YggOptions _yggOptions;

    public ProfileHandler(
        IPlayerDirectory directory,
        IPlayerCache playerCache,
        ISigningService signingService,
        YggOptions yggOptions)
    {
        _directory = directory;
        _playerCache = playerCache;
        _signingService = signingService;
        _yggOptions = yggOptions;
    }

    public async Task<ProfileResult> HandleAsync(string uuid, CancellationToken cancellationToken = default)
    {
        // 1. 尝试从缓存获取玩家状态
        var snapshot = await _playerCache.GetAsync(uuid, cancellationToken);
        if (snapshot == null)
        {
            // 缓存未命中，回源 DB
            snapshot = await LoadFromDbAsync(uuid, cancellationToken);
            if (snapshot == null)
            {
                return ProfileResult.NotFound();
            }

            // 写入缓存
            await _playerCache.SetAsync(uuid, snapshot, cancellationToken);
        }

        // 2. 生成签名 profile
        var profile = GenerateSignedProfile(uuid, snapshot);

        return ProfileResult.Success(profile, snapshot.LastModified);
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

public sealed record ProfileResult
{
    public bool IsValid { get; init; }
    public SignedProfile? Profile { get; init; }
    public long LastModified { get; init; }

    public static ProfileResult Success(SignedProfile profile, long lastModified) => new()
    {
        IsValid = true,
        Profile = profile,
        LastModified = lastModified
    };

    public static ProfileResult NotFound() => new() { IsValid = false };
}
