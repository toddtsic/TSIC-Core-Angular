using System.Collections.Concurrent;

namespace TSIC.API.Services.Teams;

/// <summary>
/// In-process async locks by string key. One API process serves every request, so these are
/// enough to make a check-then-write run one request at a time per key, in C# with no SQL
/// locking. Uncontended acquire costs microseconds; only same-key requests ever wait.
///
/// Key prefixes keep lock families apart, and the order is always registration ("reg:")
/// before create ("ag:" / "div:"), never the reverse, so they cannot deadlock. SemaphoreSlim is
/// not reentrant: a request must never acquire a key it already holds.
///
/// Origin: reopen stress test 2026-10-03 (52 club reps at once) — 2029 oversold 46/44 and
/// "WAITLIST - 2029" was minted four times in 64 ms.
/// </summary>
internal static class KeyedLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    /// <summary>Waits for the key's lock; dispose the result to release it.</summary>
    public static async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        // Idempotent: a double Dispose releases once.
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
