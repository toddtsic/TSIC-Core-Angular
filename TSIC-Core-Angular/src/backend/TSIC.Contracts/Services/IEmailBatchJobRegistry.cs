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

    /// <summary>Recipients processed so far (sent + failed) — drives the progress bar.</summary>
    public int Processed => Sent + Failed;
}
