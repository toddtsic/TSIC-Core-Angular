namespace TSIC.Contracts.Dtos.JobClone;

/// <summary>
/// Data-moved guard: thrown when the plan rebuilt inside the clone transaction no longer
/// matches the fingerprint the operator approved — source data changed between preview
/// and submit. Carries the fresh plan so the controller can return it with the 409 and
/// the workbench re-renders it for review instead of silently cloning moved data.
/// </summary>
public sealed class ClonePlanChangedException : InvalidOperationException
{
    public ClonePlanDto FreshPlan { get; }

    public ClonePlanChangedException(ClonePlanDto freshPlan)
        : base("Source data changed since the plan was previewed. Review the refreshed plan and submit again.")
    {
        FreshPlan = freshPlan;
    }
}
