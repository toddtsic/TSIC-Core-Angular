using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace TSIC.Infrastructure.Data;

/// <summary>
/// Exclusive application lock on a string resource, owned by the CURRENT transaction and
/// released at commit/rollback. Callers must already be inside
/// <see cref="DatabaseFacade.BeginTransactionAsync(CancellationToken)"/>.
///
/// SQL Server: <c>sp_getapplock</c> (LockOwner = Transaction). Postgres:
/// <c>pg_advisory_xact_lock</c> on a 64-bit hash of the resource, waited on under a
/// transaction-local <c>lock_timeout</c> that is restored straight after, so nothing else in
/// the transaction inherits it. A lock that cannot be had in time throws
/// <see cref="InvalidOperationException"/> on both.
/// </summary>
public static class DbAppLock
{
    public static async Task AcquireAsync(DatabaseFacade database, string resource, int timeoutMs, CancellationToken ct)
    {
        if (database.IsNpgsql())
        {
            try
            {
                await database.ExecuteSqlRawAsync(
                    "SELECT set_config('lock_timeout', @timeout, true); " +
                    "SELECT pg_advisory_xact_lock(hashtextextended(@resource, 0)); " +
                    "SELECT set_config('lock_timeout', '0', true);",
                    [new NpgsqlParameter("timeout", $"{timeoutMs}ms"), new NpgsqlParameter("resource", resource)],
                    ct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                throw new InvalidOperationException($"Could not acquire the application lock '{resource}' within {timeoutMs} ms.", ex);
            }
            return;
        }

        // PG-AB: SQL Server arm — delete at cutover.
        var status = new Microsoft.Data.SqlClient.SqlParameter("@status", System.Data.SqlDbType.Int)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        await database.ExecuteSqlRawAsync(
            "EXEC @status = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
            [
                new Microsoft.Data.SqlClient.SqlParameter("@resource", System.Data.SqlDbType.NVarChar, 255) { Value = resource },
                new Microsoft.Data.SqlClient.SqlParameter("@timeout", System.Data.SqlDbType.Int) { Value = timeoutMs },
                status
            ],
            ct);

        // 0 = granted, 1 = granted after waiting; negative = timed out, deadlock victim, or error.
        if (status.Value is not int lockResult || lockResult < 0)
            throw new InvalidOperationException(
                $"Could not acquire the application lock '{resource}' (sp_getapplock returned {status.Value}).");
    }
}
