using System.Threading.Channels;

namespace TSIC.API.Services.Usage;

/// <summary>
/// The seam between the request path and the database. Middleware drops a row in and
/// returns; <see cref="UsageWriterBackgroundService"/> drains it on its own thread.
/// Registered as a singleton.
///
/// BOUNDED ON PURPOSE. An unbounded channel turns a TSICLogs outage into an unbounded
/// heap: rows pile up with nothing draining them until the process dies of memory
/// pressure -- telemetry killing production, which is the exact failure this whole
/// design exists to make impossible. <see cref="BoundedChannelFullMode.DropWrite"/>
/// sheds rows instead. At the measured ~0.6 requests/sec, 10,000 slots is roughly
/// four hours of buffer and about a megabyte, so a drop means the writer is genuinely
/// dead rather than merely behind -- which is why the drop count is worth logging.
/// </summary>
public sealed class UsageQueue
{
    private const int Capacity = 10_000;

    private readonly Channel<UsageCapture> _channel;
    private long _dropped;

    public UsageQueue()
    {
        _channel = Channel.CreateBounded<UsageCapture>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <summary>
    /// Synchronous, non-blocking, never throws. This is the only thing the request
    /// path does -- there is no await here, so logging adds no latency at all rather
    /// than "a small amount". Returns false when the buffer is full.
    /// </summary>
    public bool TryWrite(in UsageCapture capture)
    {
        if (_channel.Writer.TryWrite(capture)) return true;
        Interlocked.Increment(ref _dropped);
        return false;
    }

    public ChannelReader<UsageCapture> Reader => _channel.Reader;

    /// <summary>Total rows shed because the buffer was full, for the life of the process.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Stops accepting rows and lets the reader finish what is queued. Called on
    /// shutdown so a clean recycle drains rather than discards.
    /// </summary>
    public void Complete() => _channel.Writer.TryComplete();
}
