namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Thrown when a single-elimination bracket game is scored as a tie. In this
/// format a tie is an impossible result and is rejected at entry (never
/// persisted). Subclasses <see cref="InvalidOperationException"/> so the
/// controller's existing validation catch maps it to a 400.
/// </summary>
public sealed class BracketTieRejectedException : InvalidOperationException
{
    public BracketTieRejectedException(string message) : base(message) { }
}
