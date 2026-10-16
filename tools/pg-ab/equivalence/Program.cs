// Postgres port A/B equivalence check.
//
//   dotnet run --project tools/pg-ab/equivalence [-- <outDir>]
//
// Runs every repository method the port rewrote against SQL Server (dev TSICV5/TSICLogs) and the
// local Postgres copy, writes both results as canonical JSON to <outDir> (default: a temp folder)
// and exits non-zero if any case differs. Read-only on both databases except dbErrors/unique, which
// attempts a duplicate insert inside a transaction that is always rolled back.
//
// Both databases must hold the same data: refresh Postgres with tools/pg-migrate first.
// Cases are keyed by real ids from dev data; if one stops returning rows, pick a new id.
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Infrastructure.Data;
using TSIC.Infrastructure.Data.LogsDbContext;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pg-ab-equivalence");
Directory.CreateDirectory(outDir);

var ss = await RunAllAsync("sqlserver");
var pg = await RunAllAsync("pg");
File.WriteAllText(Path.Combine(outDir, "sqlserver.json"), ss.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(outDir, "pg.json"), pg.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

var differing = ss.Select(kv => kv.Key)
    .Where(k => !JsonNode.DeepEquals(ss[k], pg[k]))
    .ToList();
var errors = ss.Concat(pg).Count(kv => kv.Value?.GetValueKind() == JsonValueKind.String && kv.Value.GetValue<string>().StartsWith("ERROR"));
Console.WriteLine($"{ss.Count} cases, {errors} errors, {differing.Count} differing. Results: {outDir}");
foreach (var k in differing) Console.WriteLine($"  DIFF {k}");
return differing.Count == 0 && errors == 0 ? 0 : 1;

static async Task<JsonObject> RunAllAsync(string provider)
{
    SqlDbContext NewCtx()
    {
        var b = new DbContextOptionsBuilder<SqlDbContext>();
        if (provider == "pg") b.UseNpgsql("Host=localhost;Database=tsicv5;Username=postgres;Search Path=dbo,public", x => x.UseNetTopologySuite());
        else b.UseSqlServer(@"Server=.\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True", x => x.UseNetTopologySuite());
        return new SqlDbContext(b.Options);
    }
    LogsDbContext NewLogs()
    {
        var b = new DbContextOptionsBuilder<LogsDbContext>();
        if (provider == "pg") b.UseNpgsql("Host=localhost;Database=tsiclogs;Username=postgres");
        else b.UseSqlServer(@"Server=.\SS2016;Database=TSICLogs;Trusted_Connection=True;TrustServerCertificate=True");
        return new LogsDbContext(b.Options);
    }

    var results = new JsonObject();
    async Task Run(string name, Func<Task<object?>> f)
    {
        try { results[name] = Canon(JsonSerializer.SerializeToNode(await f())); }
        catch (Exception ex) { results[name] = "ERROR: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
    }

    // ── Usage bucketing (EF.Functions date rewrite) ─────────────────────────────────────────
    Guid[] regJobs = [Guid.Parse("66036C7B-E1E8-4110-977F-F786353F2498"), Guid.Parse("C591D349-2D4D-444E-8E0F-70E4359FD6A7"), Guid.Parse("EB11ACA5-9222-4EE3-BC2E-D5337FEBBE96")];
    string[] roles = ["DAC0C570-94AA-4A88-8D73-6034F1F72F3A", "1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA", "6A26171F-4D94-4928-94FA-2FEFD42C3C3E", "FF4D1C27-F6DA-4745-98CC-D7E8121A5D06"];
    Guid[] usageJobs = [Guid.Parse("51CD29AB-C44D-48BA-A9CC-968252F1E118"), Guid.Parse("F5FF0B87-C9A9-444E-BB0F-3BB5ABE59BF5"), Guid.Parse("B0397924-419D-4181-B422-F3531D14E989")];
    // Aligned and deliberately unaligned `since` values (a Monday, a 1st, and mid-day mid-month).
    DateTime[] sinces = [new(2025, 1, 6), new(2024, 3, 1), new(2025, 6, 17, 13, 45, 0)];
    foreach (var since in sinces)
    {
        foreach (var bucket in Enum.GetValues<UsageBucket>())
        {
            var k = $"{since:yyyyMMddHHmm}/{bucket}";
            await Run($"regCounts/{k}", async () => await new RegistrationRepository(NewCtx()).GetRegistrationCountsByBucketAsync(regJobs, since, bucket, roles));
            await Run($"teamCounts/{k}", async () => await new TeamRepository(NewCtx()).GetTeamCountsByBucketAsync(regJobs, since, bucket));
            await Run($"usageRegs/{k}", async () => await new UsageStatsRepository(NewLogs()).GetDistinctRegistrationsByBucketAsync(usageJobs, since, bucket, null));
        }
        await Run($"paidByDay/{since:yyyyMMddHHmm}", async () => await new RegistrationRepository(NewCtx()).GetPaidRegistrationsByDayAsync(regJobs, since, [roles[3]]));
    }

    // ── Abandoned carts (DateDiffHour rewrite) ──────────────────────────────────────────────
    foreach (var storeId in new[] { 7, 8, 9 })
        foreach (var (min, max) in new[] { (0, 1_000_000), (24, 20_000), (1, 1) })
            await Run($"abandoned/{storeId}/{min}-{max}", async () => await new StoreCampaignRepository(NewCtx()).GetAbandonedCartsAsync(storeId, min, max));

    // ── Revenue rollup (DateFromParts rewrite; ADN mode reads adn.Txs) ──────────────────────
    Guid[] revenueJobs = [Guid.Parse("54063386-BA15-46F6-A073-C028009BB05A"), Guid.Parse("FC074258-EED0-4BBE-8C72-6EB2F3F15DA2"), Guid.Parse("2F1E82D9-59B3-406F-9924-21B92B7CC6F1")];
    (DateTime?, DateTime?)[] ranges = [(null, null), (new(2021, 1, 1), new(2022, 6, 30)), (new(2021, 3, 15), new(2023, 2, 1)), (null, new(2022, 12, 31)), (new(2022, 7, 1, 8, 0, 0), null)];
    foreach (var job in revenueJobs)
        foreach (var (s, e) in ranges)
        {
            await Run($"rollup/{job}/{s:yyyyMMddHH}-{e:yyyyMMddHH}", async () => await new CustomerJobRevenueRepository(NewCtx()).GetRollupAsync(job, false, s, e, []));
            await Run($"rollupAdn/{job}/{s:yyyyMMddHH}-{e:yyyyMMddHH}", async () => await new CustomerJobRevenueRepository(NewCtx()).GetRollupAsync(job, true, s, e, []));
        }

    // ── ADN month-end reconciliation (separate T-SQL / Postgres SQL), summer + winter months ──
    foreach (var (y, m) in new[] { (2026, 6), (2026, 1), (2025, 12), (2024, 7), (2023, 3), (2019, 11) })
        await Run($"adnRecon/{y}-{m:00}", async () => await new AdnReconciliationRepository(NewCtx()).GetMonthEndReconciliationAsync(m, y));

    // ── Job clone undo counts: SQL Server also counts tables the migration excluded, so only
    //    the manifest counts are compared; the ancillary walk is checked for errors only.
    foreach (var job in new[] { "66036C7B-E1E8-4110-977F-F786353F2498", "54063386-BA15-46F6-A073-C028009BB05A" })
        await Run($"cloneUndo/{job}", async () =>
        {
            var c = await new JobCloneRepository(NewCtx()).GetDevUndoCountsAsync(Guid.Parse(job));
            return new { c.AdminRegistrations, c.NonAdminRegistrations, c.RegistrationAccounting, c.Teams, c.JobFees, c.FeeModifiers, c.Bulletins, c.Agegroups, c.Divisions };
        });

    // ── App lock: a second transaction times out while the first holds; gets it after commit ──
    await Run("appLock", async () =>
    {
        await using var a = NewCtx();
        await using var b = NewCtx();
        var resource = "pg-ab:" + Guid.NewGuid();
        var steps = new List<string>();
        await using (var ta = await a.Database.BeginTransactionAsync())
        {
            await DbAppLock.AcquireAsync(a.Database, resource, 2000, default);
            await using var tb = await b.Database.BeginTransactionAsync();
            try { await DbAppLock.AcquireAsync(b.Database, resource, 1000, default); steps.Add("B acquired WHILE A HELD (WRONG)"); }
            catch (InvalidOperationException) { steps.Add("B timed out while A held"); }
            await tb.RollbackAsync();
            await ta.CommitAsync();
        }
        await using (var tb2 = await b.Database.BeginTransactionAsync())
        {
            await DbAppLock.AcquireAsync(b.Database, resource, 1000, default);
            steps.Add("B acquired after A committed");
            await tb2.RollbackAsync();
        }
        return steps;
    });

    // ── Error classification: a real duplicate-key insert (rolled back) ────────────────────
    await Run("dbErrors/unique", async () =>
    {
        await using var c = NewCtx();
        await using var tx = await c.Database.BeginTransactionAsync();
        var existing = await c.AspNetRoles.AsNoTracking().FirstAsync();
        c.AspNetRoles.Add(new TSIC.Domain.Entities.AspNetRoles { Id = existing.Id, Name = existing.Name, NormalizedName = existing.NormalizedName });
        try { await c.SaveChangesAsync(); return "no error (WRONG)"; }
        catch (DbUpdateException ex) { return $"IsUniqueViolation={DbErrors.IsUniqueViolation(ex)} IsRetryableConflict={DbErrors.IsRetryableConflict(ex)}"; }
        finally { await tx.RollbackAsync(); }
    });

    Console.WriteLine($"  {provider}: {results.Count} cases");
    return results;
}

// Canonical form: arrays sorted by element JSON (row order never matters), datetimes cut to
// microseconds (Postgres precision), decimals compared by value (0.0000 == 0).
static JsonNode? Canon(JsonNode? n) => n switch
{
    null => null,
    JsonArray a => new JsonArray(a.Select(x => Canon(x?.DeepClone())).OrderBy(x => x?.ToJsonString(), StringComparer.Ordinal).ToArray()),
    JsonObject o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, Canon(kv.Value?.DeepClone())))),
    JsonValue v when v.TryGetValue<DateTime>(out var dt) => JsonValue.Create(new DateTime(dt.Ticks - dt.Ticks % 10, dt.Kind)),
    JsonValue v when v.GetValueKind() == JsonValueKind.Number => JsonValue.Create(v.GetValue<decimal>() / 1.0000000000000000000000000000m),
    _ => n.DeepClone()
};
