using System.Text.Json.Serialization;

namespace FantasyTown.Auth.Modules.Yggdrasil.Protocol;

/// <summary>
/// Yggdrasil 协议错误类型枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum YggErrorType
{
    ForbiddenOperationException
}
