using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Creates a fresh, isolated InMemory SqlDbContext for each test.
/// The unique DB name guarantees zero state leaks between tests.
/// </summary>
public static class DbContextFactory
{
    public static SqlDbContext Create()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }
}
