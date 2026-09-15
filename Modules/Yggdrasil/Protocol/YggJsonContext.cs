using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil 协议 DTO 的 System.Text.Json 源生成上下文。
/// AOT 编译兼容，热路径零反射。
/// </summary>
[JsonSerializable(typeof(AuthenticateRequest))]
[JsonSerializable(typeof(AuthenticateResponse))]
[JsonSerializable(typeof(ValidateRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(YggErrorResponse))]
[JsonSerializable(typeof(ProfileInfo))]
[JsonSerializable(typeof(AgentInfo))]
[JsonSerializable(typeof(UserPayload))]
[JsonSerializable(typeof(UserProperty))]
[JsonSerializable(typeof(ProfileProperty))]
internal sealed partial class YggJsonContext : JsonSerializerContext;
