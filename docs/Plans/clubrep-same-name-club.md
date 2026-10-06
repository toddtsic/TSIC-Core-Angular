# Club rep: new rep for a club that already exists

Branch: `feature/clubrep-same-name-club` (from master `92d36b7ec`). Status: SCOPED, NOT BUILT. Todd gave the go to branch, not yet to build.

## Problem

A new rep replacing their club's old rep has never used TSIC. "Create NEW Club Rep Account" then dead-ends:
- An exact club-name match hard-blocks sign-up. The form stays hidden.
- The only exit is a mailto link to the club's OLDEST rep, usually the one who left.
- The old rep can't add them anyway.
- The suggested workaround (a regional suffix) creates a duplicate club.

## Background (Todd)

- The Club Team Library was meant to track a team over time. That failed in practice. It is now only a pick list so reps don't retype teams. **Priority #1: keep it as that convenience.**
- Legacy had no constraints. Occasionally two reps registered the same teams, and tournaments dealt with it. Todd accepts that risk again rather than leaving dead ends.

## Design (agreed)

### A. Create Club Rep Account
- `club-rep-register-form.component.ts`, `ClubService.RegisterAsync`.
- Show the whole form at once.
- Name matches are information, not a block:
  - "Fury Lacrosse is already on TSIC. Taking over or joining it? Go ahead. You'll have your own account, and you can bring its saved teams in when you register."
  - Each match gets a **Use this name** button.
- Remove: the red block, the "bypass them / split team history" wording, the mailto link, the regional-chapter advice.
- Claiming an empty club (no rep, no teams) stays as it is.
- Server: remove the exact-match and near-match refusals. Guard: if this user already reps a club with the same normalized name, create no second club.
- Drop rep name and email from the anonymous `GET api/clubs/search` response. It has no `[Authorize]` and no fallback policy, so anyone can harvest them today.
- **Forgot-account prompt (Todd: yes):** if the typed email matches an existing club-rep account, show one non-blocking line, "Looks like you may already have an account — reset your password." Never reveal the username.
- Relax the rename collision blocks the same way: `ClubService.RenameClubAsync` (Club & Rep Info edit) and `TeamRegistrationService.UpdateClubNameAsync`.

### B. Team wizard: the "Add a Team" row
- Live layout is `'list'` (`teams-step.component.ts`). The only add path is `team-add-row.component.ts`. The library segment, board and register modal are NOT live.
- `GetTeamsMetadataAsync` returns two new lists:
  1. **Other same-name club libraries:** active ClubTeams of OTHER clubs whose name normalized-matches the rep's club. Deduped by name + grad year, excluding teams already in the rep's own library. **All teams (Todd).**
  2. **Already in this event under another rep:** teams in this job whose other, active club-rep registration has a normalized-matching club_name. Carries team name, grad year, age group and rep name.
- Combobox gets a second, labelled group: "From another Fury Lacrosse list". Picking one prefills name, grad year and LOP and uses the EXISTING new-team path (create library row + register). No new write endpoint.
- Duplicate notice in the add-note line, and as a tag on combobox options: "Fury 2030 Blue is already registered in this event (2030) by another Fury Lacrosse rep, Jane Smith." **Show the rep's name (Todd).** Information only; Add stays enabled.

### Fix included in this build
- Director per-event club rename: `ClubRepLocalRenameService.cs:80` calls global `ClubRepository.GetByNameAsync`. With same-name clubs it can land on the other club and wrongly refuse. Resolve within the rep's own clubs.
- `RemoveClubFromRepAsync` and `UpdateClubNameAsync` share the same global lookup but have NO UI caller. Leave them.

### Unchanged (verified safe with same-name clubs)
- One-rep-per-event check (by ClubId).
- Duplicate-name-in-age-group check (per registration).
- `ResolveClubForClubRepRegistrationAsync` (by id, then the user's own clubs).
- `InitializeRegistrationAsync` (user-scoped).

## Accepted risks / out of scope
- Director trees and filters (search-teams, view-schedule, rescheduler, search-registrations) group by club_name. Two same-name reps in one event show as one club node. Display/filter only; move pickers are by RegistrationId.
- Cross-event club_name matching: the schedule QA overplay check, the team-retention report, and the TSIC-Teams Schedules tab (`TeamTournamentsRepository.cs:107-115`, global). Same-name collisions already exist; this adds more.
- A rep choosing a different name gets no duplicate notice (same as legacy).
- Normalized matching ("True" = "True Lacrosse") can show a wrong club's list. It is labelled and a suggestion only.
- SEPARATE HOLE, close separately: two API-only endpoints with no frontend caller let any signed-in user attach to any club:
  - `TeamRegistrationService.AddClubToRepAsync` (silent link on a fuzzy score of 90 or more)
  - `ClubService.AddClubAsync` with `UseExistingClubId` (no checks)

## Tests
- `TSIC.Tests/TeamRegistration/ClubRegistrationGateTests.cs` asserts the hard block and will need updating. Todd runs the tests.
