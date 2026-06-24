using System.Collections.Concurrent;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Singleton, in-memory implementation of <see cref="IEmailBatchJobRegistry"/>. Ephemeral by design
/// (see interface docs). Per-job state is mutated under a small lock so concurrent send workers can
/// fold results in safely.
/// </summary>
public sealed class EmailBatchJobRegistry : IEmailBatchJobRegistry
{
    private sealed class Entry
    {
        public required Guid JobId { get; init; }
        public required int Total { get; init; }
        public int OptedOut { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public bool Done { get; set; }
        public readonly List<string> FailedAddresses = new();
        public readonly object Gate = new();
    }

    private readonly ConcurrentDictionary<Guid, Entry> _jobs = new();

    public void Create(Guid jobId, int totalRecipients, int optedOut)
    {
        _jobs[jobId] = new Entry { JobId = jobId, Total = totalRecipients, OptedOut = optedOut };
    }

    public void RecordResult(Guid jobId, bool success, IEnumerable<string> failedAddresses)
    {
        if (!_jobs.TryGetValue(jobId, out var e)) return;
        lock (e.Gate)
        {
            if (success)
            {
                e.Sent++;
            }
            else
            {
                e.Failed++;
                foreach (var addr in failedAddresses)
                {
                    if (!e.FailedAddresses.Contains(addr)) e.FailedAddresses.Add(addr);
                }
            }
        }
    }

    public void Complete(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var e))
        {
            lock (e.Gate) { e.Done = true; }
        }
    }

    public EmailBatchJobStatus? Get(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var e)) return null;
        lock (e.Gate)
        {
            return new EmailBatchJobStatus
            {
                JobId = e.JobId,
                TotalRecipients = e.Total,
                Sent = e.Sent,
                Failed = e.Failed,
                OptedOut = e.OptedOut,
                Done = e.Done,
                FailedAddresses = e.FailedAddresses.ToList()
            };
        }
    }
}
