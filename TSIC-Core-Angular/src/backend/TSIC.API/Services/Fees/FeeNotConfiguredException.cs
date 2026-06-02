namespace TSIC.API.Services.Fees;

/// <summary>
/// Thrown when a NEW registration is stamped against a team/role whose fee is not
/// configured at any cascade level (no fees.JobFees row for team, agegroup, or job).
/// <para>
/// Distinct from a legitimately-free <b>configured</b> event ($0 with a row present,
/// FeeConfigured=true). Silently stamping $0 for an <b>unconfigured</b> team would register
/// someone for free while the UI shows a price — so we fail loud instead.
/// </para>
/// <para>
/// The player wizard pre-checks <see cref="TSIC.Contracts.Repositories.ResolvedFee.FeeConfigured"/>
/// per team and blocks just that line; team/adult/staff flows let this propagate.
/// Subclasses <see cref="InvalidOperationException"/> to match the codebase's
/// business-rule-violation idiom (e.g. "roster full").
/// </para>
/// </summary>
public sealed class FeeNotConfiguredException : InvalidOperationException
{
    public Guid JobId { get; }
    public string RoleId { get; }
    public Guid? AgegroupId { get; }
    public Guid? TeamId { get; }

    public FeeNotConfiguredException(Guid jobId, string roleId, Guid? agegroupId, Guid? teamId)
        : base($"Registration fee is not configured (job {jobId}, role {roleId}, " +
               $"agegroup {agegroupId}, team {teamId}). A fees.JobFees row must exist at the " +
               "team, agegroup, or job level before this team can take registrations.")
    {
        JobId = jobId;
        RoleId = roleId;
        AgegroupId = agegroupId;
        TeamId = teamId;
    }
}
