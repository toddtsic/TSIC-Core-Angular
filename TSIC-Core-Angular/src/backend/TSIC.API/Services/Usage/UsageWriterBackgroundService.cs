using System.Data;
using System.Threading.Channels;
using Microsoft.Data.SqlClient;
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
    private const int MaxBatchSize = 500;
    private static readonly TimeSpan LingerWindow = TimeSpan.FromSeconds(30);

    // A job's path never changes and there are ~1100 of them, so this warms once and
    // then serves every request for free. The cap exists because unresolved paths are
    // cached too (as Guid.Empty) -- without a ceiling, a bot spraying random paths
    // would grow this without limit.
    private const int MaxCachedJobPaths = 5_000;

    private readonly UsageQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UsageWriterBackgroundService> _logger;

    private readonly Dictionary<string, Guid> _jobIdByPath =
        new(StringComparer.OrdinalIgnoreCase);

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

        _logger.LogInformation(
            "UsageWriterBackgroundService started (batch<={Batch}, linger={Linger}s).",
            MaxBatchSize, LingerWindow.TotalSeconds);

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
        linger.CancelAfter(LingerWindow);

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
        var jobIds = await ResolveJobIdsAsync(batch, cancellationToken).ConfigureAwait(false);
        var teamIds = await ResolveTeamIdsAsync(batch, cancellationToken).ConfigureAwait(false);

        using var table = BuildTable(batch, jobIds, teamIds);

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
    /// jobPath to JobId, cache first. Unresolved paths are cached as Guid.Empty so a
    /// bad or retired path costs one lookup rather than one per request forever.
    /// </summary>
    private async Task<Dictionary<string, Guid>> ResolveJobIdsAsync(
        List<UsageCapture> batch,
        CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in batch)
        {
            if (!string.IsNullOrWhiteSpace(row.JobPath) && !_jobIdByPath.ContainsKey(row.JobPath))
                wanted.Add(row.JobPath);
        }

        if (wanted.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();

            // Sequential awaits, never Task.WhenAll: these share one scoped DbContext.
            foreach (var path in wanted)
            {
                var jobId = await jobs.GetJobIdByPathAsync(path, cancellationToken)
                    .ConfigureAwait(false);

                if (_jobIdByPath.Count < MaxCachedJobPaths)
                    _jobIdByPath[path] = jobId ?? Guid.Empty;
            }
        }

        return _jobIdByPath;
    }

    private async Task<Dictionary<Guid, Guid?>> ResolveTeamIdsAsync(
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

        return rows.ToDictionary(r => r.RegId, r => r.AssignedTeamId);
    }

    // ── Projection ────────────────────────────────────────────────────────────

    private static DataTable BuildTable(
        List<UsageCapture> batch,
        Dictionary<string, Guid> jobIds,
        Dictionary<Guid, Guid?> teamIds)
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
            var jobId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(row.JobPath)
                && jobIds.TryGetValue(row.JobPath, out var resolved))
                jobId = resolved;

            Guid? teamId = null;
            if (row.RegId is { } regId && teamIds.TryGetValue(regId, out var resolvedTeam))
                teamId = resolvedTeam;

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
