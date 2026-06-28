using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.JobRules;

namespace TSIC.API.Services;

/// <summary>
/// The single composer of registration-CREATE permission (see
/// <see cref="IJobRegistrationCapabilities"/>). Loads a job's facts once via
/// <see cref="IJobRepository.GetCapabilityFactsAsync"/>, folds in the eventConcluded door,
/// the director toggles, and the data preconditions, and returns the effective
/// <see cref="JobCapabilitySet"/>.
///
/// Composition (the one formula):
/// <code>
///   MUTATE(c) = door(actor) AND toggle(c) AND precondition(c)
///   door(User)  = NOT eventConcluded AND NOT superseded
///   door(Admin) = TRUE                       // session already proves now &lt; ExpiryAdmin
///   toggle(c)   = the BAllow* flag           // Admin is EXEMPT from toggles
///   precondition(c) = fees configured / teams exist  // binds even Admin (a data fact)
/// </code>
/// Fail-closed: unknown job → all-false.
/// </summary>
public sealed class JobRegistrationCapabilities : IJobRegistrationCapabilities
{
    private readonly IJobRepository _jobs;

    public JobRegistrationCapabilities(IJobRepository jobs) => _jobs = jobs;

    public async Task<JobCapabilitySet> ResolveAsync(
        Guid jobId, CapabilityActor actor, CancellationToken ct = default)
    {
        var facts = await _jobs.GetCapabilityFactsAsync(jobId, ct);
        if (facts is null)
            return Denied; // unknown job → fail closed

        // The MUTATE door. Admins are exempt (an admin session already proves now < ExpiryAdmin,
        // and post-conclusion data fix-up is legitimate admin work); ordinary users are bound
        // by "event is over" AND "a live later-year sibling exists".
        bool door;
        if (actor == CapabilityActor.Admin)
        {
            door = true;
        }
        else
        {
            var concluded = JobLifecycle.EventConcluded(
                facts.SchedulePublished,
                facts.LastGameDate,
                facts.EventEndDate,
                facts.ExpiryUsers,
                DateTime.Now);
            door = !concluded && !facts.SupersededByLaterEvent;
        }

        // Admins skip the director TOGGLES but never the data PRECONDITIONS (no fee row / no
        // team → even an admin can't price or attach the registration). Users obey both.
        bool toggle(bool flag) => actor == CapabilityActor.Admin || flag;

        return new JobCapabilitySet
        {
            CanRegisterPlayer = door && toggle(facts.AllowPlayer) && facts.PlayerFeesConfigured,
            // Staff (coach): toggle = AllowStaff; precondition = teams exist (binds admins too).
            CanRegisterStaff = door && toggle(facts.AllowStaff) && facts.TeamsExist,
            CanRegisterReferee = door && toggle(facts.AllowReferee),
            CanRegisterRecruiter = door && toggle(facts.AllowRecruiter),
            CanAddTeam = door && toggle(facts.AllowTeam && facts.ClubRepAllowAdd) && facts.ClubRepFeesConfigured,
            // Removal needs no fee/pricing precondition — only the door + the delete toggle.
            CanRemoveTeam = door && toggle(facts.AllowTeam && facts.ClubRepAllowDelete),
        };
    }

    private static readonly JobCapabilitySet Denied = new()
    {
        CanRegisterPlayer = false,
        CanRegisterStaff = false,
        CanRegisterReferee = false,
        CanRegisterRecruiter = false,
        CanAddTeam = false,
        CanRemoveTeam = false,
    };
}
