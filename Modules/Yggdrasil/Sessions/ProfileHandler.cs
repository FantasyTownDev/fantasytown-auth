using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil profile/{uuid} 处理器
/// 流程：PLAYER 缓存回源 → 签名 → 返回 profile
/// 支持 ETag=lastModified 输出缓存
/// </summary>
public sealed class ProfileHandler
{
    private readonly AuthDbContext _db;
    private readonly IPlayerCache _playerCache;
    private readonly ISigningService _signingService;
    private readonly YggOptions _yggOptions;

    public ProfileHandler(
        AuthDbContext db,
        IPlayerCache playerCache,
        ISigningService signingService,
        YggOptions yggOptions)
    {
        _db = db;
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
        var player = await _db.Players
            .Where(p => p.Uuid == uuid)
            .Select(p => new PlayerSnapshot
            {
                Name = p.Name,
                LastModified = new DateTimeOffset(p.LastModified, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                IsBanned = p.IsBanned,
                BannedUntil = _db.PlayerBans
                    .Where(b => b.Pid == p.Pid && b.IsActive)
                    .Select(b => b.BannedUntil)
                    .FirstOrDefault(),
                TexturesUrl = $"{_yggOptions.SkinBaseUrl}/textures/skins/{uuid}.png",
                TexturesSignature = null
            })
            .FirstOrDefaultAsync(cancellationToken);

        return player;
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

        // Mojang 协议签名格式：SHA1withRSA( name UTF-8 + value UTF-8 )
        var dataToSign = "textures" + valueBase64;
        var signature = _signingService.Sign(dataToSign);

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
