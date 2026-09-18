using FantasyTown.Auth.Modules.Accounts.Domain;
using FantasyTown.Auth.Modules.Accounts.Infrastructure;
using FantasyTown.Auth.Modules.Yggdrasil.Authserver;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Yggdrasil.Sessions;

/// <summary>
/// Yggdrasil hasJoined 处理器
/// 流程：GETDEL 票据 → PLAYER 缓存回源 → 签名 → 返回 profile
/// </summary>
public sealed class HasJoinedHandler
{
    private readonly AuthDbContext _db;
    private readonly ITicketService _ticketService;
    private readonly IPlayerCache _playerCache;
    private readonly ISigningService _signingService;
    private readonly YggOptions _yggOptions;

    public HasJoinedHandler(
        AuthDbContext db,
        ITicketService ticketService,
        IPlayerCache playerCache,
        ISigningService signingService,
        YggOptions yggOptions)
    {
        _db = db;
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

        // 3. 检查封禁状态
        if (snapshot.IsBanned)
        {
            var now = DateTime.UtcNow;
            var isEffectivelyBanned = snapshot.BannedUntil == null || snapshot.BannedUntil > now;
            if (isEffectivelyBanned)
            {
                return HasJoinedResult.NotFound();
            }
        }

        // 4. 生成签名 profile
        var profile = GenerateSignedProfile(uuid, snapshot);

        return HasJoinedResult.Success(profile);
    }

    private async Task<PlayerSnapshot?> LoadFromDbAsync(string uuid, CancellationToken cancellationToken)
    {
        // 回源 SQL：players LEFT JOIN 当前 active player_bans
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

public sealed record HasJoinedResult
{
    public bool IsValid { get; init; }
    public SignedProfile? Profile { get; init; }

    public static HasJoinedResult Success(SignedProfile profile) => new() { IsValid = true, Profile = profile };
    public static HasJoinedResult NotFound() => new() { IsValid = false };
}
