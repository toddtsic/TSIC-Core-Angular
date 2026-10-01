using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using TSIC.API.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Generic batch-email orchestration engine (see <see cref="IEmailBatchService"/>). Singleton: it owns
/// no scoped state and spawns background work. Pipeline: producer items -> N render workers (each its
/// OWN DI scope/DbContext) -> bounded channel -> one dispatcher per batch -> SES.
///
/// SES enforces exactly one rate rule: at most MaxSendRate RECIPIENTS per second, across the account
/// (every To/Cc/Bcc address counts). The dispatcher enforces that rule directly: each message draws its
/// address count from a one-second budget SHARED BY EVERY BATCH in this process, and its send is STARTED
/// without awaiting the SES reply — so throughput is set by the budget, not by SES round-trip latency.
/// When the second's budget is spent, the dispatcher waits for the next second.
/// </summary>
public sealed class EmailBatchService : IEmailBatchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailService _email;
    private readonly IEmailBatchJobRegistry _registry;
    private readonly IAmazonSimpleEmailService _ses;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IHostEnvironment _env;
    private readonly ILogger<EmailBatchService> _logger;
    // Unsubscribe links must point at the environment that sent them — same rule as the
    // invite/password-reset links in TextSubstitutionService/AuthController.
    private readonly string _frontendBaseUrl;

    // The per-second recipient budget. Instance state on a singleton = one budget for the whole app,
    // so concurrent batches share MaxSendRate instead of each claiming all of it.
    private readonly object _budgetLock = new();
    private long _windowStart;
    private int _windowUsed;

    public EmailBatchService(
        IServiceScopeFactory scopeFactory,
        IEmailService email,
        IEmailBatchJobRegistry registry,
        IAmazonSimpleEmailService ses,
        IHostApplicationLifetime appLifetime,
        IHostEnvironment env,
        ILogger<EmailBatchService> logger,
        Microsoft.Extensions.Options.IOptions<TSIC.API.Configuration.FrontendSettings> frontendSettings)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _registry = registry;
        _ses = ses;
        _appLifetime = appLifetime;
        _env = env;
        _logger = logger;
        _frontendBaseUrl = (frontendSettings.Value.BaseUrl ?? string.Empty).TrimEnd('/');
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

        // Opt-out is enforced HERE, uniformly for every path (no plan can skip it): partition the
        // candidate set into sendable vs opted-out and tally the latter for the summary.
        var sendable = seed.Items.Where(i => !plan.IsOptedOut(i)).ToList();
        var optedOutCount = seed.Items.Count - sendable.Count;

        _registry.Create(batchJobId, sendable.Count, optedOutCount);

        // Create the incremental audit row (Count starts at 0).
        var emailId = await CreateAuditRowAsync(plan.Audit, cancellationToken);

        // Run the pipeline in the background under the APP token (never the request token).
        _ = Task.Run(() => RunPipelineAsync(batchJobId, emailId, plan, sendable, options, _appLifetime.ApplicationStopping));

        return new EmailBatchHandle { JobId = batchJobId, TotalRecipients = sendable.Count };
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
        if (!EmailAddressRules.IsSendable(toEmail))
            return EmailBatchSummaryResult.NoRecipientEmail;

        var message = new EmailMessageDto
        {
            // Emails, not registrants (AR-087) — the same unit the Email Log row reports for this send.
            Subject = $"Batch email summary — {status.EmailsSent} email(s) sent",
            HtmlBody = BuildSummaryHtml(status),
            ToAddresses = new List<string> { toEmail.Trim() }
        };

        // Honors the same env gate as every other send (only live prod transmits).
        await _email.SendAsync(message, sendInDevelopment: false, cancellationToken);
        return EmailBatchSummaryResult.Sent;
    }

    private static string BuildSummaryHtml(EmailBatchJobStatus s)
    {
        // The list is of ADDRESSES, while the Failed row above counts registrants, so it is labelled as
        // addresses — the two can legitimately differ and must not read as the same tally (AR-087).
        var failedBlock = s.FailedAddresses.Count == 0
            ? "<p style=\"color:#2e7d32;\">No failures.</p>"
            : $"<p><strong>{s.FailedAddresses.Count}</strong> address(es) not delivered:</p><ul>{string.Join("", s.FailedAddresses.Select(a => $"<li>{System.Net.WebUtility.HtmlEncode(a)}</li>"))}</ul>";

        // AR-087: every row states its own unit. "Emails sent" leads because it is the number the sender
        // asked for and the one the Email Log records; the registrant rows keep their real unit rather
        // than being silently converted to a figure the engine cannot know (a registrant that never
        // rendered has no addresses, so there is no honest address total for the selection).
        return $"""
            <div style="font-family:Arial,sans-serif; font-size:14px; color:#222;">
                <h2 style="margin:0 0 12px;">Batch Email Summary</h2>
                <table style="border-collapse:collapse;">
                    <tr><td style="padding:2px 12px 2px 0;">Emails sent</td><td><strong>{s.EmailsSent}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Registrants selected</td><td><strong>{s.TotalRecipients}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Registrants mailed</td><td><strong>{s.Sent}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Registrants failed</td><td><strong>{s.Failed}</strong></td></tr>
                    <tr><td style="padding:2px 12px 2px 0;">Registrants opted out</td><td><strong>{s.OptedOut}</strong></td></tr>
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
        var tally = new SendTally();
        var clock = Stopwatch.StartNew();

        try
        {
            // Recipients per second this batch may start. A simulated run never transmits, so it is unmetered.
            var perSecond = simulate ? int.MaxValue : (int)Math.Max(1, Math.Floor(await ResolveMaxSendRateAsync(ct)));

            var itemChannel = Channel.CreateUnbounded<TItem>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
            var sendChannel = Channel.CreateBounded<(EmailMessageDto Message, TItem Item)>(
                new BoundedChannelOptions(options.ChannelCapacity) { SingleReader = true, SingleWriter = false });

            // Feed all items, then close the item channel.
            foreach (var item in items) itemChannel.Writer.TryWrite(item);
            itemChannel.Writer.Complete();

            // Render workers — each takes a fresh scope/DbContext per item (no cross-item tracker bloat).
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

            // Periodic audit flush while the pipeline runs.
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var flushTask = AuditFlushLoopAsync(batchJobId, emailId, sentAddresses, flushCts.Token);

            await DispatchAsync(batchJobId, sendChannel.Reader, options, simulate, perSecond, sentAddresses, tally, ct);
            await renderCompletion;

            await flushCts.CancelAsync();
            try { await flushTask; }
            catch (OperationCanceledException)
            {
                // Expected: we just cancelled the flush loop ourselves.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch email pipeline failed for job {BatchJobId}", batchJobId);
        }
        finally
        {
            var seconds = clock.Elapsed.TotalSeconds;
            _logger.LogInformation(
                "Batch email {BatchJobId} finished: {Messages} messages / {Addresses} addresses sent, {Failed} messages failed, in {Seconds:F1}s ({AddressesPerSecond:F0} addresses/s)",
                batchJobId, tally.Messages, tally.Addresses, tally.Failed, seconds,
                seconds > 0 ? tally.Addresses / seconds : 0);

            _registry.Complete(batchJobId);
            await FlushAuditAsync(emailId, batchJobId, sentAddresses, CancellationToken.None);
            await RunCompletionHookAsync(plan, batchJobId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs the plan's optional post-batch hook once, on a fresh scope, with the final status.
    /// Isolated + swallowing: a hook failure (e.g. a director-notify send) must never fault the batch.
    /// </summary>
    private async Task RunCompletionHookAsync<TItem>(EmailBatchPlan<TItem> plan, Guid batchJobId, CancellationToken ct)
    {
        if (plan.OnCompleteAsync is null) return;
        var status = _registry.Get(batchJobId);
        if (status is null) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await plan.OnCompleteAsync(status, scope.ServiceProvider, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completion hook failed for job {BatchJobId}", batchJobId);
        }
    }

    private async Task RenderWorkerAsync<TItem>(
        Guid batchJobId,
        EmailBatchPlan<TItem> plan,
        ChannelReader<TItem> items,
        ChannelWriter<(EmailMessageDto, TItem)> sink,
        CancellationToken ct)
    {
        // A FRESH scope (DbContext + repo graph) per ITEM, not per worker. Recipient resolution
        // calls a deliberately-tracked query (GetByJobAndFamilyWithUsersAsync removes AsNoTracking
        // so its entities stay editable); reusing one context across a worker's hundreds of items
        // lets the change-tracker accumulate, and EF's fixup/DetectChanges degrade as the tracked
        // graph grows — the long-lived-context-in-a-loop cliff that crawled a 10K test to ~1/sec.
        // Per-item scope disposes the context each iteration, so the tracker never piles up; it is
        // also strictly safe (each context is used by exactly one item, serially).
        await foreach (var item in items.ReadAllAsync(ct))
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var rendered = await plan.RenderAsync(item, scope.ServiceProvider, ct);
                if (rendered is null)
                {
                    _registry.RecordResult(batchJobId, false, new[] { plan.DescribeItem(item) });
                }
                else
                {
                    AppendUnsubscribeFooter(rendered); // engine owns the universal footer — every path inherits it
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

    /// <summary>
    /// Appends the canonical unsubscribe footer to the rendered body when the plan supplied a regId.
    /// Single source of the footer markup, so EVERY batch path is suppressible identically (per the
    /// "uniform, all suppressible" rule) — no path hand-rolls or omits it. The link targets the
    /// sending environment (FrontendSettings.BaseUrl), not a hardwired www.
    /// </summary>
    private void AppendUnsubscribeFooter(EmailBatchRendered rendered)
    {
        if (rendered.UnsubscribeRegId is not Guid regId) return;
        var url = $"{_frontendBaseUrl}/api/email/unsubscribe?regId={regId:D}";
        rendered.Message.HtmlBody += $"""
            <div style="margin-top:32px; padding-top:16px; border-top:1px solid #e0e0e0; text-align:center; font-size:12px; color:#999;">
                <a href="{url}" style="color:#999; text-decoration:underline;">Unsubscribe</a>
                from emails for this event
            </div>
            """;
    }

    /// <summary>
    /// The send stage: one loop per batch. For each rendered message, draw its recipient count from the
    /// shared per-second budget (waiting for the next second when it is spent), then START the send and
    /// move on without awaiting SES. Returns once the channel is drained and every started send has finished.
    /// </summary>
    private async Task DispatchAsync<TItem>(
        Guid batchJobId,
        ChannelReader<(EmailMessageDto Message, TItem Item)> source,
        EmailBatchOptions options,
        bool simulate,
        int perSecond,
        ConcurrentQueue<string> sentAddresses,
        SendTally tally,
        CancellationToken ct)
    {
        var inFlight = new List<Task>();
        await foreach (var (message, _) in source.ReadAllAsync(ct))
        {
            // Rewrite BEFORE counting, so the budget is charged for the addresses actually sent.
            var deliverToTestInbox = ApplySandboxTestInbox(message, options);

            if (!simulate) await TakeSendBudgetAsync(RecipientCount(message), perSecond, ct);
            inFlight.Add(SendAndRecordAsync(batchJobId, message, options, simulate, deliverToTestInbox, perSecond, sentAddresses, tally, ct));

            // Completed sends need not be held for the rest of a large batch.
            if (inFlight.Count >= 1000) inFlight.RemoveAll(t => t.IsCompleted);
        }
        await Task.WhenAll(inFlight);
    }

    private async Task SendAndRecordAsync(
        Guid batchJobId,
        EmailMessageDto message,
        EmailBatchOptions options,
        bool simulate,
        bool deliverToTestInbox,
        int perSecond,
        ConcurrentQueue<string> sentAddresses,
        SendTally tally,
        CancellationToken ct)
    {
        var ok = await SendOneAsync(message, options, simulate, deliverToTestInbox, perSecond, ct);
        if (ok)
        {
            foreach (var addr in message.ToAddresses) sentAddresses.Enqueue(addr);
            _registry.RecordResult(batchJobId, true, Array.Empty<string>());
            // Read from the SAME list the enqueue above walked, AFTER any test-inbox rewrite. That keeps
            // the summaries' address count identical to the EmailLogs row by construction, rather than by coincidence.
            _registry.RecordSentAddresses(batchJobId, message.ToAddresses.Count);
            Interlocked.Increment(ref tally.Messages);
            Interlocked.Add(ref tally.Addresses, message.ToAddresses.Count);
        }
        else
        {
            _registry.RecordResult(batchJobId, false, message.ToAddresses);
            Interlocked.Increment(ref tally.Failed);
        }
    }

    /// <summary>
    /// One message, up to MaxSendAttempts. The first attempt's budget was drawn by the dispatcher;
    /// every retry draws again, since it is another send as far as SES's rate rule is concerned.
    /// </summary>
    private async Task<bool> SendOneAsync(
        EmailMessageDto message,
        EmailBatchOptions options,
        bool simulate,
        bool deliverToTestInbox,
        int perSecond,
        CancellationToken ct)
    {
        if (simulate)
        {
            // 0 = no artificial delay (render-paced). On Windows Task.Delay quantizes to ~15ms,
            // so skip it entirely at 0 rather than incur a phantom 15ms/send on a large test.
            var delayMs = options.SimulatedPerUnitDelayMs!.Value;
            if (delayMs > 0) await Task.Delay(delayMs, ct);
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
            if (attempt > 1) await TakeSendBudgetAsync(RecipientCount(message), perSecond, ct);
            try
            {
                var ok = await _email.SendAsync(message, sendInDevelopment: deliverToTestInbox, ct);
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

    /// <summary>
    /// Sandbox test inbox (Staging invite testing). The invite modal exposes an editable "To (test inbox)"
    /// address ONLY in the Staging build, and RegistrationSearchService passes it ONLY for invites. When it
    /// rides the batch AND the host is sandboxed, we force a real SES send that would otherwise be suppressed
    /// and deliver every message to that single inbox — so the per-recipient token link can actually be
    /// received and clicked. Gated on IsSandbox(): in Production this never fires, so live mail is untouched.
    /// Returns whether the send must be forced through the sandbox gate.
    /// </summary>
    private bool ApplySandboxTestInbox(EmailMessageDto message, EmailBatchOptions options)
    {
        var testInbox = options.SandboxTestRecipient?.Trim();
        var deliverToTestInbox = _env.IsSandbox() && !string.IsNullOrWhiteSpace(testInbox) && testInbox.Contains('@');
        if (deliverToTestInbox)
        {
            message.CcAddresses.Clear();
            message.BccAddresses.Clear();
            message.ToAddresses = new List<string> { testInbox! };
        }
        return deliverToTestInbox;
    }

    /// <summary>What SES charges against MaxSendRate for this message: every To, Cc and Bcc address.</summary>
    private static int RecipientCount(EmailMessageDto message)
        => (message.ToAddresses?.Count ?? 0) + (message.CcAddresses?.Count ?? 0) + (message.BccAddresses?.Count ?? 0);

    /// <summary>
    /// Draws <paramref name="recipients"/> from the shared one-second budget, waiting for the next second
    /// when the current one cannot fit them. A message larger than the whole budget (only possible on the
    /// 1/sec fallback) goes alone in a fresh second rather than waiting forever.
    /// </summary>
    private async Task TakeSendBudgetAsync(int recipients, int perSecond, CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_budgetLock)
            {
                var now = Stopwatch.GetTimestamp();
                if (now - _windowStart >= Stopwatch.Frequency)
                {
                    _windowStart = now;
                    _windowUsed = 0;
                }
                if (_windowUsed == 0 || _windowUsed + recipients <= perSecond)
                {
                    _windowUsed += recipients;
                    return;
                }
                wait = TimeSpan.FromSeconds((double)(_windowStart + Stopwatch.Frequency - now) / Stopwatch.Frequency);
            }
            await Task.Delay(wait, ct);
        }
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
            // Count is EMAILS SENT, not registrants (AR-086). snap.Sent ticks once per message, and
            // one message carries a whole family's addresses, so it under-reported every fan-out;
            // sentAddresses is what fills SendTo on the same row, so the two columns now agree.
            // snap.Sent still counts registrants and still drives the progress bar; the sender-facing
            // summaries moved to the registry's address tally (EmailsSent) in AR-087, so this row and
            // those emails now quote the same figure for the same send.
            var addresses = string.Join(";", sentAddresses);
            await repo.UpdateProgressAsync(emailId, sentAddresses.Count, addresses, ct);
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

    /// <summary>Per-batch counters for the end-of-batch log line (fields, so Interlocked can update them).</summary>
    private sealed class SendTally
    {
        public int Messages;
        public int Addresses;
        public int Failed;
    }
}
