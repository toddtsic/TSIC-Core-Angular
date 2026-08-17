namespace TSIC.Contracts.Dtos.JobClone;

/// <summary>
/// An operator-facing conflict the workbench should render verbatim — today, a job path or
/// job name already taken. Exists so the controller's 409 branch catches ONLY messages
/// written for a human.
///
/// Before this type the controller caught bare <see cref="InvalidOperationException"/>, which
/// also swept up framework failures: EF's "circular dependency detected in the data to be
/// saved: 'Jobs [Added] &lt;- ForeignKeyConstraint …'" reached Ann's screen as a red panel
/// instructing her to call EnableSensitiveDataLogging. Anything not deliberately raised for
/// the operator now falls through to the 500 handler — logged for us, opaque to them.
/// </summary>
public sealed class CloneConflictException : InvalidOperationException
{
    public CloneConflictException(string message) : base(message)
    {
    }
}
