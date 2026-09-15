namespace FantasyTown.Auth.Modules.Shared;

/// <summary>
/// 统一操作结果类型，提供语义化工厂方法。
/// 用于 Application 层命令/查询处理器。
/// 领域特定结果类型（如 RegisterUserResult、LoginResult）保留在各自模块中。
/// </summary>
public sealed record Result
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; }
    public object? Data { get; init; }

    public static Result Success() => new() { IsSuccess = true, StatusCode = 200 };

    public static Result Success<T>(T data) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Data = data
    };

    public static Result Failure(string error, int statusCode = 400) => new()
    {
        IsSuccess = false,
        ErrorMessage = error,
        StatusCode = statusCode
    };

    public static Result NotFound(string error = "资源不存在") => new()
    {
        IsSuccess = false,
        ErrorMessage = error,
        StatusCode = 404
    };

    public static Result Conflict<T>(T currentData, string error = "冲突") => new()
    {
        IsSuccess = false,
        ErrorMessage = error,
        StatusCode = 409,
        Data = currentData
    };
}

/// <summary>
/// 带类型值的统一操作结果。
/// </summary>
public sealed record Result<TValue>
{
    public bool IsSuccess { get; init; }
    public TValue? Value { get; init; }
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; }

    public static Result<TValue> Success(TValue value) => new()
    {
        IsSuccess = true,
        Value = value,
        StatusCode = 200
    };

    public static Result<TValue> Failure(string error, int statusCode = 400) => new()
    {
        IsSuccess = false,
        ErrorMessage = error,
        StatusCode = statusCode
    };

    public static Result<TValue> NotFound(string error = "资源不存在") => new()
    {
        IsSuccess = false,
        ErrorMessage = error,
        StatusCode = 404
    };
}
