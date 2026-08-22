using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Queries;

/// <summary>
/// THE single definition of "what counts as a usable event address".
///
/// Two consumers need the same rule and must never disagree: FieldRepository
/// (which returns the address itself, for the Vertical Insure payload) and
/// JobRepository's pulse (which only needs to know whether one EXISTS, to decide
/// whether the buy-surfaces may advertise Team RegSaver). A second hand-written
/// copy of this predicate is exactly how the offer starts being advertised in one
/// place and refused in another.
///
/// Sourced from the field/venue bank — NEVER from a person. Sending a director's
/// own address here disclosed personal data to the carrier and had weather claims
/// adjudicated against the wrong state (AR-020).
/// </summary>
internal static class EventAddressQuery
{
    /// <summary>
    /// Complete event addresses attached to a job, best-first. Empty when the job has
    /// no usable venue — callers MUST treat that as "no policy can be written".
    ///
    /// Mirrors legacy's sourcing: league via JobLeagues on JobId, fields via
    /// FieldsLeagueSeason on LeagueId + the job's Season.
    ///
    /// Deliberately NOT filtered on JobLeagues.BIsPrimary or FieldsLeagueSeason.BActive.
    /// Legacy filtered on neither, both are unverified in live data, and either one
    /// going unset would silently strip the insurance offer from a job that sells it
    /// today. System '*' fields ARE excluded, matching every sibling field query.
    ///
    /// All four parts are required. Legacy guarded only on a non-empty street, so a row
    /// with a blank zip still reached VI and 400'd with "Invalid zip code" — the very
    /// error the director-address workaround was papering over.
    ///
    /// OrderBy(FName) does not pick a "better" venue; it only makes an inherently
    /// arbitrary choice reproducible. A multi-venue tournament has no single event
    /// address, and legacy took whatever the server returned first.
    /// </summary>
    public static IQueryable<EventAddressDto> ForJob(SqlDbContext context, Guid jobId, string season)
        => from jl in context.JobLeagues.AsNoTracking()
           join fls in context.FieldsLeagueSeason.AsNoTracking()
               on jl.LeagueId equals fls.LeagueId
           where jl.JobId == jobId
              && fls.Season == season
              && (fls.Field.FName == null || !fls.Field.FName.StartsWith("*"))
              && fls.Field.Address != null && fls.Field.Address.Trim() != ""
              && fls.Field.City != null && fls.Field.City.Trim() != ""
              && fls.Field.State != null && fls.Field.State.Trim() != ""
              && fls.Field.Zip != null && fls.Field.Zip.Trim() != ""
           orderby fls.Field.FName
           select new EventAddressDto
           {
               Street = fls.Field.Address!.Trim(),
               City = fls.Field.City!.Trim(),
               State = fls.Field.State!.Trim(),
               Zip = fls.Field.Zip!.Trim()
           };
}
