using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace TSIC.Infrastructure.Data.SqlDbContext;

// Postgres port: the scaffolded model carries SQL Server store types. Npgsql takes them literally
// (e.g. "money" binds parameters as Postgres money, which won't mix with the numeric columns
// pg-migrate creates), so on Npgsql rewrite them to the types the migration actually produced.
public partial class SqlDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        if (!Database.IsNpgsql())
            return;

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            var storeType = property.GetColumnType();
            if (storeType is null)
                continue;

            var mapped = storeType switch
            {
                "money" => "numeric(19,4)",
                "datetime" => "timestamp(3)",
                _ => Regex.Replace(storeType, @"^decimal\((\d+),\s*(\d+)\)$", "numeric($1,$2)")
            };
            if (mapped != storeType)
                property.SetColumnType(mapped);
        }
    }
}
