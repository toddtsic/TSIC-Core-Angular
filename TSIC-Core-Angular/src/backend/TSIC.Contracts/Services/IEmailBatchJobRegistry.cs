using System;
using System.Collections.Generic;

namespace TSIC.Contracts.Services;

/// <summary>
/// In-memory registry of batch-email job progress + final summary, polled by the status endpoint.
/// EPHEMERAL by design (decision): an app-pool recycle interrupts a running job; the durable record
/// of what actually went out is the incremental <c>EmailLogs</c> row, not this registry. Kept behind
/// an interface so a persisted store could replace it later with no engine change.
/// </summary>
public interface IEmailBatchJobRegistry
{
    /// <summary>Create a tracking entry for a newly accepted job.</summary>
    void Create(Guid jobId, int totalRecipients, int optedOut);

    /// <summary>Atomically fold one send outcome into the job's running totals.</summary>
    void RecordResult(Guid jobId, bool success, IEnumerable<string> failedAddresses);

    /// <summary>
    /// Atomically fold the MAILBOX count of one successfully-sent message into the job's address tally.
    /// Deliberately separate from <see cref="RecordResult"/>: that call's Sent counter ticks once per
    /// MESSAGE (one per registrant) and drives the progress bar, which stays on registrants. This is the
    /// count of mailboxes those messages actually reached — the unit the EmailLogs row records (AR-086)
    /// and therefore the unit every sender-facing summary must quote (AR-087).
    /// </summary>
    void RecordSentAddresses(Guid jobId, int addressCount);

    /// <summary>Mark the job finished (all items processed).</summary>
    void Complete(Guid jobId);

    /// <summary>Current snapshot, or null if unknown (never created, or lost to a recycle).</summary>
    EmailBatchJobStatus? Get(Guid jobId);
}

/// <summary>Snapshot of a batch job's progress / final state.</summary>
public sealed record EmailBatchJobStatus
{
    public required Guid JobId { get; init; }
    public required int TotalRecipients { get; init; }
    public required int Sent { get; init; }
    public required int Failed { get; init; }
    public required int OptedOut { get; init; }
    public required bool Done { get; init; }
    public required IReadOnlyList<string> FailedAddresses { get; init; }

    /// <summary>
    /// Mailboxes actually mailed. <see cref="Sent"/> counts MESSAGES (one per registrant); this counts
    /// the addresses those messages carried, so it exceeds Sent whenever a family fans out to two parent
    /// mailboxes. It is the same figure the EmailLogs audit row stores in Count/SendTo (AR-086), which is
    /// why the sender-facing summaries quote this and not Sent (AR-087).
    /// Not <c>required</c> on purpose: only the registry snapshot sets it, and a hand-built status
    /// (tests, a plan constructing one) legitimately defaults it to 0.
    /// </summary>
    public int EmailsSent { get; init; }

    /// <summary>Recipients processed so far (sent + failed) — drives the progress bar.</summary>
    public int Processed => Sent + Failed;
}
