using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TSIC.Contracts.Services;

/// <summary>
/// Orchestration layer ABOVE <see cref="IEmailService"/> (the SES transport, which stays thin).
/// Runs a mass batch as a background job: producer -> render workers (each own DI scope) ->
/// bounded channel -> send workers (rate-capped, retrying) -> SES. Returns immediately with a
/// job handle; callers poll <see cref="IEmailBatchJobRegistry"/> for progress + final summary.
///
/// Generic over <typeparamref name="TItem"/> so any batch path (registration-search first; roster,
/// rescheduler, ARB, ... later) plugs in as a new <see cref="EmailBatchPlan{TItem}"/> with zero
/// engine changes.
/// </summary>
public interface IEmailBatchService
{
    /// <summary>
    /// Registers a job, starts the background pipeline, and returns immediately.
    /// </summary>
    Task<EmailBatchHandle> StartAsync<TItem>(
        EmailBatchPlan<TItem> plan,
        EmailBatchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opt-in "email me this summary": composes the completion summary for <paramref name="batchJobId"/>
    /// from the in-memory job registry and sends it to <paramref name="recipientUserId"/>'s account email.
    /// Never auto-sent — only on explicit user request after a batch completes.
    /// </summary>
    Task<EmailBatchSummaryResult> EmailSummaryAsync(
        Guid batchJobId,
        string recipientUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IEmailBatchService.EmailSummaryAsync"/> — drives the controller's status code.</summary>
public enum EmailBatchSummaryResult
{
    Sent,
    UnknownJob,
    NoRecipientEmail
}

/// <summary>Returned to the caller the instant a batch is accepted — never blocks on sends.</summary>
public sealed record EmailBatchHandle
{
    public required Guid JobId { get; init; }
    public required int TotalRecipients { get; init; }
}

/// <summary>
/// Everything a batch path supplies to the engine. The engine owns iteration, opt-out skip,
/// invalid-address strip, parallelism, retry, rate-limiting, progress, and incremental audit;
/// the plan supplies only the genuinely path-specific bits.
/// </summary>
public sealed class EmailBatchPlan<TItem>
{
    /// <summary>
    /// Resolves the batch up front on the runner's scope: ALL candidate items (the engine applies
    /// <see cref="IsOptedOut"/> itself, so the seed does NOT pre-filter opt-out). Runs once, before fan-out.
    /// </summary>
    public required Func<IServiceProvider, CancellationToken, Task<EmailBatchSeed<TItem>>> SeedAsync { get; init; }

    /// <summary>
    /// Per-item opt-out test, applied by the ENGINE at seed time — uniform across every batch path.
    /// Items for which this returns true are skipped (never rendered/sent) and tallied as opted-out.
    /// Centralizing it here means no path can forget to honor unsubscribe (<c>BemailOptOut</c>).
    /// </summary>
    public required Func<TItem, bool> IsOptedOut { get; init; }

    /// <summary>
    /// Per-item, executed on a RENDER WORKER'S OWN DI SCOPE (its own DbContext + repo graph):
    /// resolve all recipient addresses, strip invalid/sentinel, and render subject+body.
    /// Return null when the item has no usable address (engine tallies it as failed-no-email).
    /// The engine appends the unsubscribe footer afterward from <see cref="EmailBatchRendered.UnsubscribeRegId"/>.
    /// </summary>
    public required Func<TItem, IServiceProvider, CancellationToken, Task<EmailBatchRendered?>> RenderAsync { get; init; }

    /// <summary>Stable label for an item used in the failed-addresses list when render/send fails.</summary>
    public required Func<TItem, string> DescribeItem { get; init; }

    /// <summary>Audit + summary metadata for the one <c>EmailLogs</c> row this batch writes.</summary>
    public required EmailBatchAudit Audit { get; init; }

    /// <summary>
    /// OPTIONAL post-batch hook the engine invokes ONCE after the pipeline drains, on a FRESH DI scope,
    /// with the final <see cref="EmailBatchJobStatus"/>. For path-specific completion side-effects
    /// (e.g. a sender summary, director notifications). Capture only plain data in the closure — resolve
    /// services from the supplied provider, never from the (by-then disposed) request scope. Null = none.
    /// Failures are logged, never thrown (they must not fault the batch).
    /// </summary>
    public Func<EmailBatchJobStatus, IServiceProvider, CancellationToken, Task>? OnCompleteAsync { get; init; }
}

/// <summary>Result of <see cref="EmailBatchPlan{TItem}.SeedAsync"/>: ALL candidate items (engine applies opt-out).</summary>
public sealed record EmailBatchSeed<TItem>
{
    public required IReadOnlyList<TItem> Items { get; init; }
}

/// <summary>A fully-rendered, ready-to-send message — plain strings, no DbContext dependency.</summary>
public sealed record EmailBatchRendered
{
    public required EmailMessageDto Message { get; init; }

    /// <summary>
    /// Registration whose public unsubscribe link the engine appends as the standard footer, so
    /// every batch email is suppressible identically. Null = no footer (path has no per-recipient regId).
    /// </summary>
    public Guid? UnsubscribeRegId { get; init; }
}

/// <summary>Metadata for the incremental <c>EmailLogs</c> audit row.</summary>
public sealed record EmailBatchAudit
{
    public required Guid JobId { get; init; }
    public required string? SenderUserId { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
    public string? SendFrom { get; init; }
}

/// <summary>Engine tuning. Defaults are conservative; structure supports more without re-architecture.</summary>
public sealed record EmailBatchOptions
{
    /// <summary>Render-stage parallelism. Default 1 (serial; render-wins keep it fast enough).</summary>
    public int RenderWorkers { get; init; } = 1;

    /// <summary>Send-stage parallelism. Clamped to SES MaxSendRate at runtime.</summary>
    public int SendWorkers { get; init; } = 8;

    /// <summary>Bounded channel capacity between render and send (caps in-flight rendered bodies).</summary>
    public int ChannelCapacity { get; init; } = 256;

    /// <summary>Max SES send attempts per message (incl. first). Backoff between attempts.</summary>
    public int MaxSendAttempts { get; init; } = 3;

    /// <summary>
    /// DEV/SANDBOX ONLY. When set, the send step sleeps this long instead of transmitting — lets the
    /// progress UI be exercised end-to-end without real sends. Ignored outside sandbox.
    /// </summary>
    public int? SimulatedPerUnitDelayMs { get; init; }

    /// <summary>
    /// DEV/SANDBOX ONLY. With simulation on, fail every Nth message deterministically so the
    /// failed-addresses panel can be exercised (dev sends are otherwise no-op successes).
    /// </summary>
    public int? SyntheticFailEveryN { get; init; }

    /// <summary>
    /// SANDBOX ONLY. Test inbox. When set (and the host is sandboxed), the send step forces a REAL
    /// SES send that would otherwise be suppressed and delivers each message to this single address
    /// instead of its real recipients. Lets an invite-token link be delivered and clicked. Ignored in Production.
    /// </summary>
    public string? SandboxTestRecipient { get; init; }
}
