using System.Data;
using System.Threading.Channels;
using Microsoft.Data.SqlClient;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Usage;

/// <summary>
/// Drains <see cref="UsageQueue"/>, resolves the dimensions that need a database, and
/// bulk-inserts into logs.AppUsage. Everything expensive about usage logging happens
/// here, on this thread, away from any request.
///
/// Enrichment is per BATCH, never per row. A batch of a few hundred requests contains
/// far fewer distinct job paths and registrations, so this costs one query per
/// dimension instead of one round-trip per logged row. Resolving inline in the request
/// pipeline instead would have added two or more TSICV5 queries to every single API
/// call, which is the trade this whole design exists to refuse.
///
/// No EF here. SqlBulkCopy over a raw connection also sidesteps the lifetime problem a
/// singleton hosted service would otherwise have with a scoped DbContext.
/// </summary>
public sealed class UsageWriterBackgroundService : BackgroundService
{
    // Volume is ~0.6 requests/sec measured, so these are about batching the ENRICHMENT
    // queries, not about insert throughput. A long linger means more rows share one
    // lookup; the cost is that rows sit in memory a little longer before landing, which
    // for usage telemetry is not a cost at all.
    //
    // MaxBatchSize stays a const on purpose: it is a memory-safety ceiling, not a
    // tuning dial, and at this volume it never fires.
    private const int MaxBatchSize = 500;

    /// <summary>
    /// Usage:LingerSeconds. Configurable ONLY because the linger is the whole
    /// feedback loop when testing: at 30s a developer hits an endpoint, queries
    /// logs.AppUsage, sees nothing, and concludes the feature is broken. Dev runs
    /// short so the row lands while the tester is still looking at it.
    ///
    /// The trade, stated so it is not discovered later: a short linger flushes
    /// batches of one, so a dev box never exercises the multi-row enrichment path
    /// (distinct-path dedup, the JobId cache, the batched TeamId lookup) -- which is
    /// exactly where an enrichment bug would live. Fire a burst of requests to test
    /// that path, or raise the value.
    /// </summary>
    private const string LingerConfigKey = "Usage:LingerSeconds";
    private const int DefaultLingerSeconds = 30;

    // Clamped, not trusted. A typo'd 0 would turn this into a per-row hot loop
    // against TSICV5 -- the enrichment queries are the expensive part, and firing
    // them once per row is the exact failure this whole design exists to avoid.
    private const int MinLingerSeconds = 1;
    private const int MaxLingerSeconds = 300;

    // NO CACHE HERE, deliberately. An earlier version kept a process-lifetime
    // Dictionary<jobPath, JobId> with negative entries and a 5,000 cap. It was removed:
    //
    //   * It bought nothing. The lookup is an Index Seek on UI_JOBPATH over ~1,100 rows
    //     that live permanently in buffer cache -- verified against the actual plan.
    //     Batching the round-trips is the whole saving; remembering answers between
    //     batches is not.
    //   * It was the one thing in this subsystem an anonymous stranger could degrade.
    //     Misses had to be cached too (or one junk path costs a query forever), and
    //     misses are attacker-supplied: ~1,100 real paths left ~3,900 slots for garbage
    //     from any crawler walking /api/jobs/{made-up}. Once full it accepted nothing
    //     further -- real jobs included -- and never evicted, so the cost it existed to
    //     remove came back permanently.
    //
    // Do not reintroduce it. Per-batch dedup below gives the same benefit with no state
    // to size, expire, or attack.
    private readonly UsageQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UsageWriterBackgroundService> _logger;

    /// <summary>
    /// Resolved once in ExecuteAsync, before the loop. Not re-read per batch: a linger
    /// that changed underneath a running drain would be a moving target for no gain.
    /// </summary>
    private TimeSpan _lingerWindow = TimeSpan.FromSeconds(DefaultLingerSeconds);

    private long _written;
    private long _lastReportedDrops;

    public UsageWriterBackgroundService(
        UsageQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<UsageWriterBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Closes the queue before the base implementation cancels the stopping token, so a
    /// clean recycle drains what is buffered instead of discarding it. A hard kill
    /// still loses the buffer -- the accepted trade for never touching a request.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _configuration.GetConnectionString("LogsConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning(
                "UsageWriterBackgroundService not started: LogsConnection is not configured.");
            return;
        }

        _lingerWindow = ResolveLingerWindow();

        _logger.LogInformation(
            "UsageWriterBackgroundService started (batch<={Batch}, linger={Linger}s).",
            MaxBatchSize, _lingerWindow.TotalSeconds);

        var reader = _queue.Reader;
        var batch = new List<UsageCapture>(MaxBatchSize);

        // Reads until the channel COMPLETES rather than until the token cancels, so the
        // buffer drains on shutdown. The host's own shutdown timeout bounds this.
        while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            batch.Clear();
            while (batch.Count < MaxBatchSize && reader.TryRead(out var first))
                batch.Add(first);

            if (batch.Count == 0) continue;

            // Hold the batch open briefly so more rows join it, unless we are stopping.
            if (batch.Count < MaxBatchSize && !stoppingToken.IsCancellationRequested)
                await LingerAsync(batch, stoppingToken).ConfigureAwait(false);

            try
            {
                await FlushAsync(batch, connectionString, stoppingToken).ConfigureAwait(false);
                _written += batch.Count;
            }
            catch (Exception ex)
            {
                // An unhandled exception from a BackgroundService stops the host by
                // default. Usage logging must never be able to do that, so the batch is
                // reported and dropped rather than retried -- a poison batch would
                // otherwise loop forever.
                _logger.LogError(ex,
                    "Usage batch of {Count} rows discarded after a write failure.", batch.Count);
            }

            ReportDropsIfChanged();
        }

        _logger.LogInformation(
            "UsageWriterBackgroundService stopped. Rows written={Written}, dropped={Dropped}.",
            _written, _queue.DroppedCount);
    }

    private async Task LingerAsync(List<UsageCapture> batch, CancellationToken stoppingToken)
    {
        using var linger = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        linger.CancelAfter(_lingerWindow);

        try
        {
            while (batch.Count < MaxBatchSize
                   && await _queue.Reader.WaitToReadAsync(linger.Token).ConfigureAwait(false))
            {
                while (batch.Count < MaxBatchSize && _queue.Reader.TryRead(out var more))
                    batch.Add(more);
            }
        }
        catch (OperationCanceledException)
        {
            // Linger elapsed, or shutdown began. Either way the batch is ready.
        }
        catch (ChannelClosedException)
        {
            // Queue completed during shutdown; flush what we have.
        }
    }

    /// <summary>
    /// Reads Usage:LingerSeconds and clamps it into range. An absent key is normal and
    /// silent -- the default is the production value. An out-of-range or unparseable
    /// value is NOT silent: it is a typo in an overlay, and a linger silently different
    /// from the one written in the file is exactly the kind of thing that gets
    /// diagnosed as "the writer is broken" months later.
    /// </summary>
    private TimeSpan ResolveLingerWindow()
    {
        var configured = _configuration.GetValue<int?>(LingerConfigKey);
        if (configured is null) return TimeSpan.FromSeconds(DefaultLingerSeconds);

        var clamped = Math.Clamp(configured.Value, MinLingerSeconds, MaxLingerSeconds);
        if (clamped != configured.Value)
        {
            _logger.LogWarning(
                "{Key}={Configured} is outside {Min}-{Max}s; using {Clamped}s.",
                LingerConfigKey, configured.Value, MinLingerSeconds, MaxLingerSeconds, clamped);
        }

        return TimeSpan.FromSeconds(clamped);
    }

    private void ReportDropsIfChanged()
    {
        var dropped = _queue.DroppedCount;
        if (dropped == _lastReportedDrops) return;

        _logger.LogWarning(
            "Usage queue dropped {New} row(s) (total {Total}). The buffer is full, which " +
            "means the writer is failing or stalled -- not merely behind.",
            dropped - _lastReportedDrops, dropped);
        _lastReportedDrops = dropped;
    }

    // ── Enrichment ────────────────────────────────────────────────────────────

    private async Task FlushAsync(
        List<UsageCapture> batch,
        string connectionString,
        CancellationToken cancellationToken)
    {
        // Two lookups for one mixed batch: signed-in rows resolve from their
        // registration, anonymous rows from their jobPath. Sequential awaits, never
        // Task.WhenAll -- each opens its own scope, and concurrent DbContext use is the
        // failure this codebase has a standing rule against.
        var registrations = await ResolveRegistrationDimensionsAsync(batch, cancellationToken).ConfigureAwait(false);
        var jobIds = await ResolveJobIdsAsync(batch, cancellationToken).ConfigureAwait(false);

        using var table = BuildTable(batch, jobIds, registrations);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "logs.AppUsage",
            BatchSize = table.Rows.Count,
        };

        // Mapped by NAME, and Id is deliberately absent -- it is IDENTITY. Because the
        // table carries no DEFAULT constraints, a column missing from this list makes
        // the insert fail loudly instead of silently recording a wrong value as fact.
        foreach (DataColumn column in table.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulk.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// jobPath to JobId, for the ANONYMOUS rows of this batch only.
    ///
    /// Rows carrying a regId are excluded: their JobId comes back from the registration
    /// lookup, which is both already happening and more authoritative -- the token is
    /// job-scoped, so the registration's own foreign key names the job in use, while
    /// jobPath is a string claim minted at login. Asking about them here would be a
    /// second query for something already in hand.
    ///
    /// One round-trip per batch, not one per path. Paths that do not resolve are absent
    /// from the dictionary and land as Guid.Empty at projection -- not remembered, so a
    /// crawler spraying invented paths costs one row in one query and nothing after.
    /// </summary>
    private async Task<Dictionary<string, Guid>> ResolveJobIdsAsync(
        List<UsageCapture> batch,
        CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in batch)
        {
            if (row.RegId is null && !string.IsNullOrWhiteSpace(row.JobPath))
                wanted.Add(row.JobPath);
        }

        if (wanted.Count == 0) return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        var rows = await jobs.GetJobIdsByPathsAsync(wanted, cancellationToken)
            .ConfigureAwait(false);

        // Keyed the same way the captures are matched -- case-insensitively, because
        // jobPath comparison is case-insensitive everywhere else in the system and the
        // database may return a different casing than the request supplied.
        var resolved = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            resolved[row.JobPath] = row.JobId;

        return resolved;
    }

    /// <summary>
    /// Both registration-derived dimensions -- JobId and TeamId -- for the SIGNED-IN
    /// rows of this batch, in one query.
    ///
    /// JobId rides along because it is on the same row: fetching it here costs nothing
    /// beyond a column, and it is the authoritative job for authenticated traffic.
    /// Nothing is cached. TeamId must not be (a player's team assignment changes, and a
    /// remembered value would record stale attribution), and JobId need not be, since
    /// it arrives free in a query already being made.
    /// </summary>
    private async Task<Dictionary<Guid, RegistrationUsageDimensionsDto>> ResolveRegistrationDimensionsAsync(
        List<UsageCapture> batch,
        CancellationToken cancellationToken)
    {
        var regIds = new HashSet<Guid>();
        foreach (var row in batch)
        {
            if (row.RegId is { } regId) regIds.Add(regId);
        }

        if (regIds.Count == 0) return [];

        using var scope = _scopeFactory.CreateScope();
        var registrations = scope.ServiceProvider.GetRequiredService<IRegistrationRepository>();

        var rows = await registrations
            .GetRegistrationUsageDimensionsAsync(regIds, cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.RegId);
    }

    // ── Projection ────────────────────────────────────────────────────────────

    private static DataTable BuildTable(
        List<UsageCapture> batch,
        Dictionary<string, Guid> jobIds,
        Dictionary<Guid, RegistrationUsageDimensionsDto> registrations)
    {
        var table = new DataTable();
        table.Columns.Add("OccurredAt", typeof(DateTime));
        table.Columns.Add("AppClientId", typeof(int));
        table.Columns.Add("PlatformId", typeof(int));
        table.Columns.Add("AppVersion", typeof(string));
        table.Columns.Add("Controller", typeof(string));
        table.Columns.Add("Action", typeof(string));
        table.Columns.Add("QueryString", typeof(string));
        table.Columns.Add("StatusCode", typeof(short));
        table.Columns.Add("UserId", typeof(string));
        table.Columns.Add("RegId", typeof(Guid));
        table.Columns.Add("JobId", typeof(Guid));
        table.Columns.Add("TeamId", typeof(Guid));
        table.Columns.Add("IsBot", typeof(bool));
        table.Columns.Add("BrowserId", typeof(int));
        table.Columns.Add("DeviceClassId", typeof(int));

        foreach (var row in batch)
        {
            var (appClientId, platformId, appVersion) =
                UsageClassifier.ParseClientTag(row.ClientTag);
            var (isBot, browserId, deviceClassId) =
                UsageClassifier.ClassifyUserAgent(row.UserAgent);

            // Guid.Empty is the fact table's explicit "no job context" member, not a
            // null and not a missing row. An unresolvable path lands here the same way
            // an absent one does -- by design, so JobId can stay NOT NULL.
            //
            // The registration wins when there is one. It is the same job the token was
            // scoped to, read from the row's own foreign key rather than from a string
            // claim, so the two cannot drift; jobPath is what anonymous traffic has
            // INSTEAD, not a second opinion to reconcile.
            var jobId = Guid.Empty;
            Guid? teamId = null;

            if (row.RegId is { } regId && registrations.TryGetValue(regId, out var dimensions))
            {
                jobId = dimensions.JobId;
                teamId = dimensions.AssignedTeamId;
            }
            else if (!string.IsNullOrWhiteSpace(row.JobPath)
                     && jobIds.TryGetValue(row.JobPath, out var resolvedJobId))
            {
                jobId = resolvedJobId;
            }

            table.Rows.Add(
                row.OccurredAt,
                appClientId,
                platformId,
                appVersion,
                Truncate(row.Controller, 50),
                Truncate(row.Action, 60),
                (object?)row.QueryString ?? DBNull.Value,
                row.StatusCode,
                (object?)row.UserId ?? DBNull.Value,
                (object?)row.RegId ?? DBNull.Value,
                jobId,
                (object?)teamId ?? DBNull.Value,
                isBot,
                browserId,
                deviceClassId);
        }

        return table;
    }

    // Controller and Action come from route metadata and are comfortably inside the
    // column widths today. Clamped anyway so one long action name can never fail the
    // insert of an entire batch.
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
