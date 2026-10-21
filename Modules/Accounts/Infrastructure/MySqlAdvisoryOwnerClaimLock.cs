using System.Data;
using System.Data.Common;
using FantasyTown.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FantasyTown.Auth.Modules.Accounts.Infrastructure;

/// <summary>
/// MariaDB 咨询锁实现（审计 C1）：
/// GET_LOCK 是会话级锁——在 DbContext 底层连接上获取后，同一上下文的后续操作
/// 复用同一已打开的连接，确保"无 ServerOwner 判定 → User+Player 落库"全程持锁；
/// 连接关闭（含异常路径）自动释放，不会遗留死锁
/// </summary>
public sealed class MySqlAdvisoryOwnerClaimLock : IOwnerClaimLock
{
    private const string LockName = "ft_seed_owner";
    private const int AcquireTimeoutSeconds = 5;

    private readonly AuthDbContext _db;

    public MySqlAdvisoryOwnerClaimLock(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<T> RunExclusiveAsync<T>(Func<bool, Task<T>> action, CancellationToken cancellationToken = default)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var acquired = false;
        try
        {
            acquired = await TryAcquireAsync(connection, cancellationToken);
            return await action(acquired);
        }
        finally
        {
            if (acquired)
            {
                await ReleaseAsync(connection);
            }

            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TryAcquireAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, @timeout)";
        AddNameParameter(command);

        var timeoutParam = command.CreateParameter();
        timeoutParam.ParameterName = "@timeout";
        timeoutParam.Value = AcquireTimeoutSeconds;
        command.Parameters.Add(timeoutParam);

        // GET_LOCK 返回 1=获锁 / 0=超时 / NULL=错误
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long value && value == 1;
    }

    private static async Task ReleaseAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RELEASE_LOCK(@name)";
        AddNameParameter(command);
        await command.ExecuteScalarAsync();
    }

    private static void AddNameParameter(DbCommand command)
    {
        var nameParam = command.CreateParameter();
        nameParam.ParameterName = "@name";
        nameParam.Value = LockName;
        command.Parameters.Add(nameParam);
    }
}
