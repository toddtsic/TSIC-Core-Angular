using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Generic batch-email orchestration engine (see <see cref="IEmailBatchService"/>). Singleton: it owns
/// no scoped state and spawns background work. Pipeline: producer items -> N render workers (each its
/// OWN DI scope/DbContext) -> bounded channel -> M send workers (rate-capped, retrying) -> SES.
/// Render runs serial-within-worker so each worker's DbContext is never used concurrently.
/// </summary>
public sealed class EmailBatchService : IEmailBatchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailService _email;
    private readonly IEmailBatchJobRegistry _registry;
    private readonly IAmazonSimpleEmailService _ses;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<EmailBatchService> _logger;

    public EmailBatchService(
        IServiceScopeFactory scopeFactory,
        IEmailService email,
        IEmailBatchJobRegistry registry,
        IAmazonSimpleEmailService ses,
        IHostApplicationLifetime appLifetime,
        ILogger<EmailBatchService> logger)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _registry = registry;
        _ses = ses;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public async Task<EmailBatchHandle> StartAsync<TItem>(
        EmailBatchPlan<TItem> plan,
        EmailBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        var batchJobId = Guid.NewGuid();

        // Seed up front on its own scope (one query for the whole batch).
        EmailBatchSeed<TItem> seed;
        using (var seedScope = _scopeFactory.CreateScope())
        {
            seed = await plan.SeedAsync(seedScope.ServiceProvider, cancellationToken);
        }

        _registry.Create(batchJobId, seed.Items.Count, seed.OptedOutCount);

        // Create the incremental audit row (Count starts at 0).
        var emailId = await CreateAuditRowAsync(plan.Audit, cancellationToken);

        // Run the pipeline in the background under the APP token (never the request token).
        _ = Task.Run(() => RunPipelineAsync(batchJobId, emailId, plan, seed.Items, options, _appLifetime.ApplicationStopping));

        return new EmailBatchHandle { JobId = batchJobId, TotalRecipients = seed.Items.Count };
    }

    public async Task<EmailBatchSummaryResult> EmailSummaryAsync(
        Guid batchJobId, string recipientUserId, CancellationToken cancellationToken = default)
    {
        var status = _registry.Get(batchJobId);
        if (status is null) return EmailBatchSummaryResult.UnknownJob;

        string? toEmail;
        using (var scope = _scopeFactory.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await users.GetByIdAsync(recipientUserId, cancellationToken);
            toEmail = user?.Email;
        }
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
            return EmailBatchSummaryResult.NoRecipientEmail;

        var message = new EmailMessageDto
        {
            Subject = $"Batch email summary — {status.Sent} sent",
            HtmlBody = BuildSummaryHtml(status),
            ToAddresses = new List<string> { toEmail.Trim() }
        };

        // Honors the same env gate as every other send (only live prod transmits).
        await _email.SendAsync(message, sendInDevelopment: false, cancellationToken);
        return EmailBatchSummaryResult.Sent;
    }

    private static string BuildSummaryHtml(EmailBatchJobStatus s)
    {
        var failedBlock = s.FailedAddresses.Count == 0
            ? "<p style=\"color:#2e7d32;\">No failures.</p>"
            : $"<p><strong>{s.FailedAddresses.Count}</strong> failed:</p><ul>{string.Join("", s.FailedAddresses.Select(a => $"<li>{System.Net.WebUtility.HtmlEncode(a)}</li>"))}</ul>";

        return $"""
            <div style="font-family:Arial,sans-serif; font-size:14px; color:#222;">
                <h2 style="margin:0 0 12px;">Batch Email Summary</h2>
                <table style="border-collapse:collapse;">
                    <tr><td style="padding:2px 12px 2px 0;">Total recipients</td><td><strong>{s.TotalRecipients}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Sent</td><td><strong>{s.Sent}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Failed</td><td><strong>{s.Failed}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Opted out</td><td><strong>{s.OptedOut}</strong></td></tr>
                </table>
                <div style="margin-top:12px;">{failedBlock}</div>
            </div>
            """;
    }

    private async Task RunPipelineAsync<TItem>(
        Guid batchJobId,
        int emailId,
        EmailBatchPlan<TItem> plan,
        IReadOnlyList<TItem> items,
        EmailBatchOptions options,
        CancellationToken ct)
    {
        // The simulate flag's PRESENCE alone forces the no-transmit path, independent of any
        // environment detection: when set, the send step sleeps + records synthetic results and
        // NEVER calls _email.SendAsync or the SES client. A flagged request can only reach here in
        // a sandbox env (the controller rejects it in Production), so decoupling from IsSandbox()
        // means a TEST request can never produce a real send even if the host env were misread.
        var simulate = options.SimulatedPerUnitDelayMs.HasValue;
        var sentAddresses = new ConcurrentQueue<string>();

        try
        {
            var sendWorkers = await ResolveSendConcurrencyAsync(options, simulate, ct);
            var pacer = new SendPacer(simulate ? double.MaxValue : await ResolveMaxSendRateAsync(ct));

            var itemChannel = Channel.CreateUnbounded<TItem>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
            var sendChannel = Channel.CreateBounded<(EmailMessageDto Message, TItem Item)>(
                new BoundedChannelOptions(options.ChannelCapacity) { SingleReader = false, SingleWriter = false });

            // Feed all items, then close the item channel.
            foreach (var item in items) itemChannel.Writer.TryWrite(item);
            itemChannel.Writer.Complete();

            // Render workers — each owns its scope/DbContext for its lifetime, used serially.
            var renderWorkers = Math.Max(1, options.RenderWorkers);
            var renderTasks = Enumerable.Range(0, renderWorkers)
                .Select(_ => RenderWorkerAsync(batchJobId, plan, itemChannel.Reader, sendChannel.Writer, ct))
                .ToArray();

            // When all render workers finish, close the send channel.
            var renderCompletion = Task.Run(async () =>
            {
                await Task.WhenAll(renderTasks);
                sendChannel.Writer.Complete();
            }, ct);

            // Send workers.
            var sendTasks = Enumerable.Range(0, sendWorkers)
                .Select(_ => SendWorkerAsync(batchJobId, sendChannel.Reader, options, simulate, pacer, sentAddresses, ct))
                .ToArray();

            // Periodic audit flush while the pipeline runs.
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var flushTask = AuditFlushLoopAsync(batchJobId, emailId, sentAddresses, flushCts.Token);

            await renderCompletion;
            await Task.WhenAll(sendTasks);

            flushCts.Cancel();
            try { await flushTask; } catch (OperationCanceledException) { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch email pipeline failed for job {BatchJobId}", batchJobId);
        }
        finally
        {
            _registry.Complete(batchJobId);
            await FlushAuditAsync(emailId, batchJobId, sentAddresses, CancellationToken.None);
        }
    }

    private async Task RenderWorkerAsync<TItem>(
        Guid batchJobId,
        EmailBatchPlan<TItem> plan,
        ChannelReader<TItem> items,
        ChannelWriter<(EmailMessageDto, TItem)> sink,
        CancellationToken ct)
    {
        // One scope (one DbContext + repo graph) per worker, reused serially across its items.
        using var scope = _scopeFactory.CreateScope();
        await foreach (var item in items.ReadAllAsync(ct))
        {
            try
            {
                var rendered = await plan.RenderAsync(item, scope.ServiceProvider, ct);
                if (rendered is null)
                {
                    _registry.RecordResult(batchJobId, false, new[] { plan.DescribeItem(item) });
                }
                else
                {
                    await sink.WriteAsync((rendered.Message, item), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Render failed for item {Item} in job {BatchJobId}", plan.DescribeItem(item), batchJobId);
                _registry.RecordResult(batchJobId, false, new[] { plan.DescribeItem(item) });
            }
        }
    }

    private async Task SendWorkerAsync<TItem>(
        Guid batchJobId,
        ChannelReader<(EmailMessageDto Message, TItem Item)> source,
        EmailBatchOptions options,
        bool simulate,
        SendPacer pacer,
        ConcurrentQueue<string> sentAddresses,
        CancellationToken ct)
    {
        await foreach (var (message, _) in source.ReadAllAsync(ct))
        {
            var ok = await SendOneAsync(message, options, simulate, pacer, ct);
            if (ok)
            {
                foreach (var addr in message.ToAddresses) sentAddresses.Enqueue(addr);
                _registry.RecordResult(batchJobId, true, Array.Empty<string>());
            }
            else
            {
                _registry.RecordResult(batchJobId, false, message.ToAddresses);
            }
        }
    }

    private async Task<bool> SendOneAsync(
        EmailMessageDto message,
        EmailBatchOptions options,
        bool simulate,
        SendPacer pacer,
        CancellationToken ct)
    {
        if (simulate)
        {
            await Task.Delay(options.SimulatedPerUnitDelayMs!.Value, ct);
            // Deterministic synthetic failures so the failed-addresses panel can be exercised.
            if (options.SyntheticFailEveryN is int n && n > 0)
            {
                var bucket = Interlocked.Increment(ref _simCounter);
                if (bucket % n == 0) return false;
            }
            return true;
        }

        var attempts = Math.Max(1, options.MaxSendAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await pacer.WaitAsync(ct);
            try
            {
                var ok = await _email.SendAsync(message, sendInDevelopment: false, ct);
                if (ok) return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SES send attempt {Attempt}/{Max} threw", attempt, attempts);
            }
            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct); // linear backoff
            }
        }
        return false;
    }

    private int _simCounter;

    private async Task AuditFlushLoopAsync(Guid batchJobId, int emailId, ConcurrentQueue<string> sentAddresses, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await FlushAuditAsync(emailId, batchJobId, sentAddresses, ct);
        }
    }

    private async Task FlushAuditAsync(int emailId, Guid batchJobId, ConcurrentQueue<string> sentAddresses, CancellationToken ct)
    {
        try
        {
            var snap = _registry.Get(batchJobId);
            if (snap is null) return;
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEmailLogRepository>();
            await repo.UpdateProgressAsync(emailId, snap.Sent, string.Join(";", sentAddresses), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit flush failed for job {BatchJobId}", batchJobId);
        }
    }

    private async Task<int> CreateAuditRowAsync(EmailBatchAudit audit, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmailLogRepository>();
        var entry = new EmailLogs
        {
            JobId = audit.JobId,
            Count = 0,
            Subject = audit.Subject,
            Msg = audit.BodyTemplate,
            SendFrom = audit.SendFrom,
            SendTo = string.Empty,
            SenderUserId = audit.SenderUserId,
            SendTs = DateTime.Now
        };
        await repo.LogAsync(entry, ct);
        return entry.EmailId;
    }

    private async Task<int> ResolveSendConcurrencyAsync(EmailBatchOptions options, bool simulate, CancellationToken ct)
    {
        if (simulate) return Math.Max(1, options.SendWorkers);
        var rate = await ResolveMaxSendRateAsync(ct);
        var cap = (int)Math.Max(1, Math.Floor(rate));
        return Math.Clamp(options.SendWorkers, 1, cap);
    }

    private async Task<double> ResolveMaxSendRateAsync(CancellationToken ct)
    {
        try
        {
            var quota = await _ses.GetSendQuotaAsync(new GetSendQuotaRequest(), ct);
            if (quota.MaxSendRate is double rate && rate > 0) return rate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read SES MaxSendRate; using conservative default");
        }
        return 1.0; // conservative fallback
    }

    /// <summary>
    /// Paces send-starts to at most <c>maxPerSecond</c> across all send workers (token spacing).
    /// </summary>
    private sealed class SendPacer
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly long _intervalTicks;
        private long _nextAllowed;

        public SendPacer(double maxPerSecond)
        {
            if (double.IsInfinity(maxPerSecond) || maxPerSecond <= 0)
            {
                _intervalTicks = 0;
            }
            else
            {
                _intervalTicks = (long)(Stopwatch.Frequency / maxPerSecond);
            }
            _nextAllowed = Stopwatch.GetTimestamp();
        }

        public async Task WaitAsync(CancellationToken ct)
        {
            if (_intervalTicks == 0) return;
            await _gate.WaitAsync(ct);
            try
            {
                var now = Stopwatch.GetTimestamp();
                if (_nextAllowed > now)
                {
                    var waitSeconds = (_nextAllowed - now) / (double)Stopwatch.Frequency;
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
                    now = Stopwatch.GetTimestamp();
                }
                _nextAllowed = now + _intervalTicks;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
