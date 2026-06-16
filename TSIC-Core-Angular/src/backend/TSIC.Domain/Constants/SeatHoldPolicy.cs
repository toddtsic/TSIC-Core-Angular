using System;

namespace TSIC.Domain.Constants;

/// <summary>
/// The clock that governs a provisional roster seat (a pending registration, <c>BActive=0</c>).
///
/// A seat counts toward a team's <c>MaxCount</c> when EITHER the registration is a confirmed
/// member (<c>BActive=1</c> — paid, pay-by-check, or free; counts forever) OR it is an in-flight
/// reservation whose <c>RegistrationTs</c> is still inside this window. Past the window an
/// abandoned cart stops counting, so its seat frees itself — while a confirmed member never lapses.
///
/// Single source of truth for the window so the two capacity counters, the atomic acquire, and
/// the frontend countdown all agree. Fixed (no sliding renewal): re-picking a team does not extend
/// the window; only re-acquiring a lapsed seat at the payment step restamps <c>RegistrationTs</c>.
/// </summary>
public static class SeatHoldPolicy
{
    /// <summary>How long a provisional seat hold survives, in minutes. Fixed at 30 (decided).</summary>
    public const int WindowMinutes = 30;

    /// <summary>
    /// The capacity cutoff: a provisional reservation counts only when its <c>RegistrationTs</c>
    /// is strictly greater than this. Computed in C# (a captured value) so it is sent to SQL as a
    /// parameter rather than translated to <c>GETDATE()</c> mid-query — every row in one count is
    /// then measured against the same instant. Uses <c>DateTime.Now</c> (local AZ) to match how
    /// <c>RegistrationTs</c> is stamped on creation.
    /// </summary>
    public static DateTime Cutoff() => DateTime.Now.AddMinutes(-WindowMinutes);
}
