using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Creates a fresh, isolated InMemory SqlDbContext for each test.
/// The unique DB name guarantees zero state leaks between tests.
///
/// TransactionIgnoredWarning is downgraded to ignore so tests can exercise services
/// that wrap multi-step writes in BeginTransaction/Commit (e.g. JobCloneService).
/// InMemory has no real transactions; the calls are no-ops, which is acceptable for
/// behavior-level tests that assert on final state, not rollback semantics.
/// </summary>
public static class DbContextFactory
{
    public static SqlDbContext Create()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new SqlDbContext(options);
    }
}
