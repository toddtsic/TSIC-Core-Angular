using System;
using System.Linq.Expressions;
using TSIC.Domain.Entities;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The single, reusable definition of a job's <b>login/list door</b> — "is this job
/// still within its access window for this actor class". These are the canonical
/// replacements for the ~11 inline <c>DateTime.Now &lt; j.ExpiryUsers</c> /
/// <c>&lt; j.ExpiryAdmin</c> comparisons scattered across the role-list and job-list
/// repositories.
///
/// They are <see cref="Expression{TDelegate}"/> (NOT methods) so they compose into EF
/// <c>IQueryable.Where(...)</c> and translate to SQL. <c>DateTime.Now</c> is left inside
/// the tree on purpose: EF re-translates it to <c>GETDATE()</c> per query, so the same
/// static instance is correct on every call (an async method like
/// <c>IsJobExpiredForUsersAsync</c> CANNOT be used here — it can't be embedded in a
/// SQL <c>where</c>).
///
/// IMPORTANT: this is the EXPIRY door (ExpiryUsers / ExpiryAdmin), the generous
/// data-access window used for login/role-offer/list filtering and SETTLE. It is NOT
/// the "event is over" signal that gates CREATE — that is
/// <see cref="JobLifecycle.EventConcluded"/>. Do not conflate them: a concluded event
/// can still be within its expiry window (rosters viewable, balances payable).
/// </summary>
public static class JobExpiry
{
    /// <summary>Job is still open to ordinary users (now &lt; ExpiryUsers). Mirrors the
    /// legacy inline filters exactly; null-safe by the non-nullable column type.</summary>
    public static readonly Expression<Func<Jobs, bool>> NotExpiredForUsers =
        j => DateTime.Now < j.ExpiryUsers;

    /// <summary>Job is still open to admins (now &lt; ExpiryAdmin). The wider window
    /// admins keep after the public door (ExpiryUsers) has closed.</summary>
    public static readonly Expression<Func<Jobs, bool>> NotExpiredForAdmin =
        j => DateTime.Now < j.ExpiryAdmin;
}
