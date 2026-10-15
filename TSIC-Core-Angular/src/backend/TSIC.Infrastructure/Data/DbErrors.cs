using Npgsql;

namespace TSIC.Infrastructure.Data;

/// <summary>
/// Classifies database errors the same way on SQL Server and Postgres. Pass the exception as
/// caught (DbUpdateException or the provider exception itself); the chain is walked.
/// </summary>
public static class DbErrors
{
    /// <summary>Unique constraint/index violation. SQL Server 2627/2601; Postgres 23505.</summary>
    public static bool IsUniqueViolation(Exception ex) => Any(ex,
        pg => pg.SqlState == PostgresErrorCodes.UniqueViolation,
        ss => ss.Number is 2627 or 2601);

    /// <summary>
    /// The transaction lost a concurrency race and can be retried as-is. SQL Server deadlock
    /// victim (1205); Postgres deadlock (40P01) or Serializable conflict (40001), which is how
    /// SERIALIZABLE transactions that SQL Server would deadlock usually fail on Postgres.
    /// </summary>
    public static bool IsRetryableConflict(Exception ex) => Any(ex,
        pg => pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure,
        ss => ss.Number == 1205);

    private static bool Any(Exception ex, Func<PostgresException, bool> pg, Func<Microsoft.Data.SqlClient.SqlException, bool> ss)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException p && pg(p)) return true;
            if (e is Microsoft.Data.SqlClient.SqlException s && ss(s)) return true; // PG-AB: SQL Server arm
        }
        return false;
    }
}
