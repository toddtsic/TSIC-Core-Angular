using System;
using System.Collections.Generic;
using TSIC.Domain.Constants;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The registration-visibility predicate, in ONE place, with its reasoning kept.
///
/// A "Register Player" / "Register a Team" card on the public site is governed by five
/// clauses, of which the director's toggle is one. The other four are enforced server-side
/// and were reported to the UI as a single boolean — so a director could turn registration
/// on, see the save succeed, and get no card, with nothing anywhere saying why. That silence
/// is what this class exists to end: <see cref="Compose"/> yields the same booleans the public
/// pulse has always shipped, and <see cref="Describe"/> yields the SAME clauses as prose for
/// the admin readout. Two consumers, one evaluation — the readout cannot drift from the site
/// it explains, because it is not a second implementation.
///
/// Pure and clock-injected (<c>now</c>): no DB, no DateTime.Now, so it is testable and the
/// caller decides which clock (the server's, always — see <see cref="JobLifecycle"/>).
/// </summary>
public static class RegistrationReadiness
{
    /// <summary>
    /// The minimum facts both consumers need. Every field here is already materialized by
    /// <c>JobRepository.GetJobPulseAsync</c>; nothing was added to the public pulse payload
    /// to support the readout.
    /// </summary>
    public sealed record CoreFacts
    {
        // ── Lifecycle (the create "door") ──
        public required bool SchedulePublished { get; init; }
        public DateTime? LastGameDate { get; init; }
        public DateTime? EventEndDate { get; init; }
        public required DateTime ExpiryUsers { get; init; }
        public required bool Superseded { get; init; }

        // ── Player ──
        public required bool PlayerToggleOn { get; init; }
        public required bool PlayerFeesConfigured { get; init; }
        public required bool PlayerTeamsAvailable { get; init; }

        // ── Team ──
        public required bool TeamToggleOn { get; init; }
        public required bool TeamFeesConfigured { get; init; }
    }

    /// <summary>
    /// The composed verdicts. <see cref="PlayerRegistrationOpen"/> and
    /// <see cref="TeamRegistrationOpen"/> are EXACTLY the two pulse fields of those names —
    /// same inputs, same composition — so re-anchoring the pulse on this type changes no
    /// behaviour for its consumers (invite guard, player wizard, header task menu).
    ///
    /// Note that <see cref="PlayerRegistrationOpen"/> deliberately EXCLUDES team availability:
    /// the pulse ships that as its own field and the frontend ANDs the two in
    /// <c>isPlayerRegistrationEffectivelyOpen</c>. <see cref="PlayerCardVisible"/> is that AND.
    /// </summary>
    public sealed record Verdicts
    {
        public required bool EventConcluded { get; init; }
        public required bool Superseded { get; init; }

        /// <summary>Create door: nothing new may be registered when this is false.</summary>
        public bool DoorOpen => !EventConcluded && !Superseded;

        public required bool PlayerRegistrationOpen { get; init; }
        public required bool PlayerTeamsAvailable { get; init; }

        /// <summary>What the public "Register Player" card actually requires.</summary>
        public bool PlayerCardVisible => PlayerRegistrationOpen && PlayerTeamsAvailable;

        public required bool TeamRegistrationOpen { get; init; }
    }

    /// <summary>One rendered clause of the predicate: the answer AND the evidence.</summary>
    public sealed record Clause
    {
        /// <summary>Stable key for the UI (icon/anchor/fix-link routing).</summary>
        public required string Key { get; init; }
        public required string Label { get; init; }
        public required bool Passed { get; init; }

        /// <summary>The evidence — real dates and counts, never a restatement of the label.</summary>
        public required string Detail { get; init; }

        /// <summary>
        /// Which screen fixes a failing clause: <c>scheduling</c>, <c>toggle</c>, <c>fees</c>,
        /// <c>teams</c> — or null when the clause is explanatory and has no local fix.
        /// </summary>
        public string? FixTarget { get; init; }
    }

    public static class ClauseKeys
    {
        public const string EventNotOver = "event-not-over";
        public const string NotSuperseded = "not-superseded";
        public const string ToggleOn = "toggle-on";
        public const string FeesConfigured = "fees-configured";
        public const string TeamAvailable = "team-available";
        public const string NotALeague = "not-a-league";
    }

    public static class FixTargets
    {
        public const string Scheduling = "scheduling";
        public const string Toggle = "toggle";
        public const string Fees = "fees";
        public const string Teams = "teams";
    }

    /// <summary>
    /// Compose the verdicts. This is the function the public pulse folds its own
    /// registration flags through — see <c>JobRepository.GetJobPulseAsync</c> step 3.
    /// </summary>
    public static Verdicts Compose(CoreFacts f, DateTime now)
    {
        var concluded = JobLifecycle.EventConcluded(
            f.SchedulePublished, f.LastGameDate, f.EventEndDate, f.ExpiryUsers, now);
        var door = !concluded && !f.Superseded;

        return new Verdicts
        {
            EventConcluded = concluded,
            Superseded = f.Superseded,
            PlayerRegistrationOpen = f.PlayerToggleOn && f.PlayerFeesConfigured && door,
            PlayerTeamsAvailable = f.PlayerTeamsAvailable,
            TeamRegistrationOpen = f.TeamToggleOn && f.TeamFeesConfigured && door,
        };
    }

    /// <summary>
    /// The extra evidence the readout renders but the pulse has no use for — counts and the
    /// window dates that turn "no team is available" into "40 teams, none inside their
    /// registration window; the last one closed 2026-06-30".
    /// </summary>
    public sealed record DescribeFacts
    {
        public required int JobTypeId { get; init; }

        /// <summary>Teams on the job, any state — separates "none built" from "all closed".</summary>
        public required int TeamsTotal { get; init; }

        /// <summary>Active, self-rostering, non-bucket teams — ignoring the date window.</summary>
        public required int TeamsSelfRosterEligible { get; init; }

        /// <summary>Of those, how many contain <c>now</c> in their registration window.</summary>
        public required int TeamsInWindowNow { get; init; }

        /// <summary>Latest window close among eligible teams — "it closed on…".</summary>
        public DateTime? LatestTeamWindowClose { get; init; }

        /// <summary>Soonest window open still in the future — "it opens on…".</summary>
        public DateTime? NextTeamWindowOpen { get; init; }

        public string? SupersedingJobName { get; init; }
    }

    /// <summary>Player clauses, in evaluation order. Always the full list — a passing clause
    /// teaches the rule as much as a failing one, and a list that changes shape is unreadable.</summary>
    public static IReadOnlyList<Clause> DescribePlayer(
        CoreFacts f, DescribeFacts d, Verdicts v, DateTime now) =>
    [
        EventNotOverClause(f, v, now),
        NotSupersededClause(d, v),
        ToggleClause(f.PlayerToggleOn, "Player"),
        FeesClause(f.PlayerFeesConfigured, "player", "a player registration"),
        TeamAvailableClause(d),
    ];

    /// <summary>Team clauses, in evaluation order.</summary>
    public static IReadOnlyList<Clause> DescribeTeam(
        CoreFacts f, DescribeFacts d, Verdicts v, DateTime now)
    {
        var isLeague = d.JobTypeId == JobConstants.JobTypeLeague;
        return
        [
            EventNotOverClause(f, v, now),
            NotSupersededClause(d, v),
            ToggleClause(f.TeamToggleOn, "Team"),
            FeesClause(f.TeamFeesConfigured, "club-rep", "a team registration"),
            new Clause
            {
                Key = ClauseKeys.NotALeague,
                Label = "This event registers teams",
                Passed = !isLeague,
                Detail = isLeague
                    ? "This is a LEAGUE. Leagues never show a \"Register a Team\" link — players "
                      + "self-roster onto teams the league has already built."
                    : "Teams register themselves on this event type.",
            },
        ];
    }

    /// <summary>Whether the public "Register a Team" card can render — every team clause passing.</summary>
    public static bool TeamCardVisible(Verdicts v, DescribeFacts d) =>
        v.TeamRegistrationOpen && d.JobTypeId != JobConstants.JobTypeLeague;

    // ── Shared clauses ────────────────────────────────────────────────────────

    private static Clause ToggleClause(bool on, string audience) => new()
    {
        Key = ClauseKeys.ToggleOn,
        Label = $"{audience} registration is turned on",
        Passed = on,
        Detail = on
            ? $"\"Allow {audience} Registration\" is on."
            : $"\"Allow {audience} Registration\" is off — turn it on below.",
        FixTarget = on ? null : FixTargets.Toggle,
    };

    private static Clause FeesClause(bool configured, string roleWord, string whatItPrices) => new()
    {
        Key = ClauseKeys.FeesConfigured,
        Label = $"{char.ToUpperInvariant(roleWord[0])}{roleWord[1..]} fees are configured",
        Passed = configured,
        Detail = configured
            ? $"A {roleWord} fee row exists, so {whatItPrices} can be priced."
            : $"No {roleWord} fee row exists for this job. A $0 fee counts — but with no row at "
              + $"all {whatItPrices} cannot be priced, so the link stays hidden.",
        FixTarget = configured ? null : FixTargets.Fees,
    };

    private static Clause EventNotOverClause(CoreFacts f, Verdicts v, DateTime now)
    {
        // Name the signal that DECIDED it — the hierarchy is invisible otherwise, and after a
        // clone the deciding signal is nearly always a year-shifted EventEndDate that landed
        // in the past. "Your event ended 14 days ago per its end date" is the whole diagnosis.
        // The rung comes from JobLifecycle.Resolve, the same walk that produced the verdict —
        // re-deriving it here would be a second copy of the hierarchy, free to disagree.
        var (_, signal, date) = JobLifecycle.Resolve(
            f.SchedulePublished, f.LastGameDate, f.EventEndDate, f.ExpiryUsers, now);

        var signalLabel = signal switch
        {
            JobLifecycle.ConcludedSignal.LastGame => "the last scheduled game",
            JobLifecycle.ConcludedSignal.EventEnd => "the event end date",
            _ => "the user expiry date",
        };
        // A published schedule's last game is fixed by the schedule, not by a settings screen —
        // there is nothing to send the director to. The other two rungs are editable dates.
        var fix = signal == JobLifecycle.ConcludedSignal.LastGame ? null : FixTargets.Scheduling;

        string detail;
        if (!v.EventConcluded)
        {
            detail = $"Still running — {signalLabel} is {date:yyyy-MM-dd}.";
        }
        else
        {
            var days = (now.Date - date.Date).Days;
            var ago = days == 1 ? "1 day ago" : $"{days} days ago";
            detail = $"The event reads as OVER: {signalLabel} is {date:yyyy-MM-dd} ({ago}). While "
                     + "that is true, no registration link appears no matter which toggles are on.";
        }

        return new Clause
        {
            Key = ClauseKeys.EventNotOver,
            Label = "The event is not over",
            Passed = !v.EventConcluded,
            Detail = detail,
            FixTarget = v.EventConcluded ? fix : null,
        };
    }

    private static Clause NotSupersededClause(DescribeFacts d, Verdicts v) => new()
    {
        Key = ClauseKeys.NotSuperseded,
        Label = "No later event has replaced this one",
        Passed = !v.Superseded,
        Detail = v.Superseded
            ? $"\"{d.SupersedingJobName}\" is a later-year event in this series and is open, so "
              + "visitors are sent there instead. Rename or close that event if this one is the "
              + "live one."
            : "No later-year event in this series is taking registrations.",
    };

    private static Clause TeamAvailableClause(DescribeFacts d)
    {
        var passed = d.TeamsInWindowNow > 0;
        string detail;

        if (passed)
        {
            detail = $"{d.TeamsInWindowNow} of {d.TeamsSelfRosterEligible} self-rostering team(s) "
                     + "are inside their registration window right now.";
        }
        else if (d.TeamsTotal == 0)
        {
            detail = "This job has no teams yet. A player registers ONTO a team, so the link "
                     + "cannot appear until teams exist.";
        }
        else if (d.TeamsSelfRosterEligible == 0)
        {
            detail = $"{d.TeamsTotal} team(s) exist, but none are active with self-rostering "
                     + "allowed (on the team or its agegroup).";
        }
        else if (d.NextTeamWindowOpen.HasValue)
        {
            detail = $"{d.TeamsSelfRosterEligible} self-rostering team(s), none open yet — the "
                     + $"earliest window opens {d.NextTeamWindowOpen.Value:yyyy-MM-dd}.";
        }
        else if (d.LatestTeamWindowClose.HasValue)
        {
            detail = $"{d.TeamsSelfRosterEligible} self-rostering team(s), all CLOSED — the last "
                     + $"window closed {d.LatestTeamWindowClose.Value:yyyy-MM-dd}. After a clone "
                     + "these dates carry the previous season's window, shifted a year.";
        }
        else
        {
            detail = $"{d.TeamsSelfRosterEligible} self-rostering team(s), none inside their "
                     + "registration window right now.";
        }

        return new Clause
        {
            Key = ClauseKeys.TeamAvailable,
            Label = "A team is open for players to join",
            Passed = passed,
            Detail = detail,
            FixTarget = passed ? null : FixTargets.Teams,
        };
    }
}
