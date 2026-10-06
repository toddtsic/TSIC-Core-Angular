# Club rep: new rep for a club that already exists

Branch: `feature/clubrep-same-name-club`. The plan was reviewed step by step with Todd on 2026-10-06, and he gave the go.

The branch also carries Todd's schedule appearance commits (games grid, view-schedule). That's by his ruling: they reach master with this feature.

## Problem

A new rep replacing their club's old rep has never used TSIC. "Create NEW Club Rep Account" then dead-ends:
- An exact club-name match hard-blocks sign-up, and the form stays hidden.
- The only exit is a mailto link to the club's OLDEST rep, usually the one who left.
- The old rep can't add them anyway.
- The suggested workaround (a regional suffix) creates a duplicate club.

## Background (Todd)

- The Club Team Library was meant to track a team over time, and that failed in practice. It is now a pick list so reps don't retype teams. **Priority #1: keep it as that convenience.**
- Legacy had no constraints. Occasionally two reps registered the same teams, and tournaments dealt with it. Todd accepts that risk again rather than leaving dead ends. It is surfaced early instead: the rep gets a confirm-before-adding, and the director gets a CADT badge.

## Design (as ruled)

"Same club name" everywhere below means `ClubNameMatcher.IsSameClubName`:
- Normalized, so "Fury Lax" = "Fury Lacrosse" ("lacrosse", "lax", "lc", "club" are filler).
- A filler-only name ("Lacrosse Club") matches nothing.

### 1. Sign-up server (`ClubService.RegisterAsync`)
- A club name never refuses sign-up.
- The rep is added as **a** rep of the new club; others can be added later.
- **Silent claim:** an unclaimed EMPTY club (no reps, no library teams) whose name is EXACTLY the typed name (case/space-insensitive, not merely normalized) is claimed instead of duplicated. It's safe because a wrong claimant inherits nothing.
- **Same-rep guard:** an existing user who already reps a club with the same club name gets no second club.
- Request lost `ExistingClubId` and `ConfirmedNewClub`. Response lost `SimilarClubs`.

### 2. "Already have an account?" (frontend only)
- No email lookup. A live lookup on a public form would let anyone test whether an email is a club rep.
- A line shown to everyone at the top of the form: "Already have a club rep account? Sign in or reset your password." It links to the same forgot-password screen as the sign-in page.

### 3. Club search (anonymous `GET api/clubs/search`)
- No longer returns rep name or email. Only the sign-up form read them.

### 4. Club & Rep Info rename (`ClubService.RenameClubAsync`)
- Another club's name is allowed.
- It refuses only a collision with ANOTHER of the same rep's clubs.
- The lock once teams are registered is unchanged.
- `update-club-name` (no UI caller) is left alone.

### 5. Director per-event rename (`ClubRepLocalRenameService`)
- Checks the rep's own clubs FIRST, then refuses only if another club has the name. Names aren't unique, so the old global lookup could hit the wrong club.
- The "another rep in this event already uses this name" refusal is REMOVED. One rule: same-name reps in an event are allowed.

### 6. Teams step data (`GetTeamsMetadataAsync`; rules in `SameNameClubLists`)
- `SameNameLibraryTeams`: every other same-name club's ACTIVE library teams.
  - Deduped by name + grad year.
  - Excludes the rep's own library, archived own rows included.
  - Tagged with the source club. All teams, per Todd.
- `SameNameEventTeams`: teams in THIS event under other active club-rep registrations with the same club_name.
  - On-the-books only: active, not DROPPED. Waitlisted teams are included.
  - Covers any number of reps, each tagged with the rep's name.

### 7. Regenerate API models.

### 8. Sign-up screen (`club-rep-register-form.component.ts`)
- Whole form visible from the start.
- Matches show a friendly panel: "Fury Lacrosse is already on TSIC. Taking over or joining it? Go ahead…".
  - Each match has a **Use this name** button, which guarantees the step-9 match.
  - No rep names or emails.
- Silent claim, so there's no claim panel.
- Friendly, welcoming language throughout. No red, no block.

### 9. Add a Team row (`team-add-row.component.ts`, the live `'list'` layout)
- The combobox gains a second section, "From another Fury Lacrosse list".
- Picking from it fills name, grad year and LOP. Add registers the team and saves the rep's own library copy, using the existing new-team path.
- A rep taking over from a predecessor registers the predecessor's teams by click.

### 10. Duplicate warning (same row): forceful, never a dead end
- An amber warning with an icon: "Fury 2030 Blue looks like it's already registered in this event by another Fury Lacrosse rep, Jane Smith. Registering it again creates a second entry and a second fee."
- Add asks first: **Don't add** (the default, with focus) or "Yes — it's a different team, add it".
- Combobox options carry "⚠ Registered here by Jane Smith".

### 11. Tests
- Sign-up gate rewritten to the new rules.
- Director rename: own club found when another shares its name; a name another rep in the event uses is allowed.
- `TeamsMetadataSameNameTests` cover the list rules and the event query.

### 12. CADT badge (pure CADT fix)
- `CadtClubNode.RepCount` = distinct club-rep registrations under that club name, set by the 3 builders that feed screens:
  - team search
  - registration search
  - `job-filter-tree`, which serves view-schedule, rescheduler and public rosters
- `job-filter-tree` is anonymous, so RepCount is zeroed unless the caller is a Director/SuperDirector/Superuser of THAT job.
- The shared `cadt-tree-filter` shows "⚠ 2 reps" on the club line, with a tooltip.
- Grouping, filtering and node ids are unchanged. Truly separate nodes were deferred: they would change filter contracts on game-day screens.

## Accepted risks / out of scope
- Director trees still group by club name. The badge is the warning.
- Cross-event club_name matching (schedule QA overplay, team-retention report, TSIC-Teams Schedules tab): same-name collisions already exist, and this adds more.
- A rep choosing a different name gets no warning (same as legacy).
- SEPARATE HOLE, close separately: two API-only endpoints let any signed-in user attach to any club, `TeamRegistrationService.AddClubToRepAsync` and `ClubService.AddClubAsync` with `UseExistingClubId`.

## Status: all 12 steps done, on the branch (not merged, not pushed)
- Server: `f4ff45abf`. Full backend suite 1230/1230.
- Models regenerated: `296a1aaea`.
- Frontend: `1415da054` and walkthrough fixes in `08102d7da`. `npm run build` passes all 4 prebuild gates.
- Frontend spec `team-add-row.same-name.spec.ts` passes.
  - The neighbouring `teams-step.component.spec.ts` fails 4 tests. That's pre-existing: its state mock has no `applyTeamsMetadata`.
- Browser walkthrough on local (no writes; never submitted, never confirmed an add):
  - Sign-up: whole form visible, friendly panel, no mailto, "Use this name" works, "Sign in" link returns to the gate.
  - Teams step (madii_maas, lftc-summer-2027): "From another True Lacrosse list" section, 21 teams. A pick reads "saved to your library too".
  - Teams step (TopTierSports): typing cclacrosse1's "Top Tier National 2029" shows the amber warning naming Erin Abbot-Gillin. Add asks, "Don't add" has focus, and Don't add keeps the row.
  - CADT: Search Teams (director) shows "⚠ 2 reps" on Top Tier National. The anonymous job-filter-tree carries no rep counts. A director's job-filter-tree carries them.
- Found in the walkthrough: real prod data has a blank-named library team (ClubTeamId 16165, club 2297). It seeded an empty Add row with a bogus note and LOP. Guarded in `08102d7da`.
- Real duplicate found: lftc-summer-2027 "Top Tier National 2029/2030" is registered twice, by the same person under two accounts (TopTierSports, cclacrosse1).

## Review fixes after the Playwright pass (Todd 2026-10-06)
1. **The duplicate warning compares by AGE GROUP, not team name.** The same team carries different names on different lists ("Top Tier National 2029" in the event vs "2029" in a library), so a name match missed the real case. Once the Add row has a name and an age group, other same-name reps' teams in that age group are listed, rep by rep, and Add asks first. A WAITLIST twin counts as its age group. The name-based "Registered here by…" tags in the dropdown are gone: one rule, no name matching. Accepted cost: a club fielding two teams in one age group through two reps answers the question once. The comparison uses the age-group NAME, waitlist prefix stripped (the server already sends it stripped), not the ID. Waitlist twins have different IDs.
2. **Club rename re-stamps the rep's teamless event registrations.** Their club name follows, and so do Assignment and RegistrationCategory where they still carry the old name. A registration with any team keeps its name (the lock is unchanged). One save.
3. **Sign-up lists same-name clubs only** (`ClubNameMatcher.IsSameClubName`, the same rule as the Teams step's same-name lists). Look-alikes are no longer listed.
- Verified: backend suite 1231/1231 (new rename test); `npm run build` passes all 4 prebuild gates; `team-add-row.same-name.spec.ts` 7/7. Playwright:
  - zztest_f2 in lftc-summer-2027, 2029: lists both Erin accounts' teams. 2030: the same. 2031: nothing. The confirm asks "Is ZZ Fresh Name a different team from those?" with focus on Don't add. 390px: no overflow.
  - Sign-up: "top tier national" lists the 3 Top Tier National clubs. "Top Tier", "Lacrosse Club" and "LC" list nothing.
  - zztest_f7: a rename moves the registration's club_name, and renaming back restores it.

## Revision: "Is this your club?" copies the picked club's library (Todd 2026-10-06)
Names aren't a club's identity: two unrelated clubs can share one. So the Teams step no longer offers every same-name club's teams.
- **Sign-up:** when same-name clubs exist, the rep answers "Is this your club?" by picking one club (by ClubId) or **None of these — we're a new club**. Create Account waits for an answer. Picking fills in that club's spelling, and editing the name clears the answer.
- **Server:** `ClubRepRegistrationRequest.SourceClubId`. The server refuses a club that isn't the same club name, then copies its ACTIVE library teams into the rep's club (one per name + grad year, skipping ones the rep already has and blank names). This happens once; nothing links the clubs afterwards. No DDL.
- **Removed:** `SameNameLibraryTeams`/`SameNameLibraryTeamDto`, `GetClubIdNamesAsync`/`ClubIdName`, and the Add row's "From another … list". The Add row is back to master's list plus the age-group warning.
- **Still name-based:** the duplicate warning (`SameNameEventTeams`).
- **Verified:**
  - Backend: 1230/1231 pass. The one failure is `EventBrowseTests` "Expired job with a game in the past 9 months", which `c78b33aa4` broke (the test sets no EventEndDate).
  - `npm run build`: all 4 prebuild gates pass.
  - Playwright:
    - zztest_f4 picked Top Tier National (IN). Club 2287's 4 teams were copied into club 2469 with grad year and LOP, and the Teams step offers them as their own.
    - zztest_f5 chose None of these and got an empty club 2470.
    - A brand-new name gets no question.
    - The age-group warning is unchanged.

## Revision: the exact club name is the identity (Todd 2026-10-06)
One rule: the loose same-name match (`ClubNameMatcher.IsSameClubName`) only helps a rep FIND their club at sign-up; after that the EXACT club name is the identity.
- **Sign-up:** picking a club from "Is your club one of these?" locks the Club Name box to that club's exact name (read-only, with a line: pick **None of these** to use a different name). Picking another club switches it; **None of these** unlocks it and puts back what the rep had typed. The server stamps the picked club's exact name on the new club, whatever the request typed.
- **Duplicate warning** (`SameNameClubLists.EventTeams`): exact `club_name`, compared ordinally — the way the director's CADT trees group clubs — so the rep's warning and the director's "⚠ N reps" badge always agree. A merely similar name ("Lax Plus Club" vs "Lax Plus") is a different club.
- **Registered Teams** groups by rep when other reps share the club name: "You (name)" first in green with every action; other reps read-only in grey, "Registered and paid for by X — not on your bill." Payment shows only the rep's own teams.
- **Duplicate warning UI:** facts, the question, the bold fee line, and **Don't add** / **Yes, add it** together as soon as it shows; + Add and ✕ are held until it is answered. Don't add clears the row.
- Verified: backend 1236/1237 (the one failure is EventBrowse, from `c78b33aa4`, not this branch); `npm run build` passes the gates.

## Revision: one place to rename the club — the Teams step, gated on THIS event (Todd 2026-10-06)
- **Where:** a "Registering as **{club}**" bar at the top of the Teams step, with **Rename** while this event holds none of the rep's teams (dropped included). Otherwise a lock: "To rename your club for this event, ask the event director." The Club card on Club Rep Info is read-only, and shows the event's club name (it was missing for a rep who arrived already signed in).
- **Server gate** (`ClubService.RenameClubAsync`, now given the wizard token's regId): the rep's registration in THIS event must be one of their teamless registrations under the club's name. Teams in other events no longer lock it — each event's registration keeps the name it was registered under. Refused with no event in hand.
- **Effect:** renames the club (library, future events) and re-stamps the rep's teamless registrations under the old name, this event's among them. The Teams step reloads, so the duplicate warning and the other reps' groups follow the new name.
- **Director rename never collides:** it needs a team in the event; a team here locks the rep's rename; a director-renamed registration no longer carries the club's name, so the rep's rename never re-stamps it.
- **Known consequence:** the team-retention report matches clubs across years by name; a renamed club reads as two there (already an accepted risk above).
- Verified: backend 1238/1239 (EventBrowse, from `c78b33aa4`); `npm run build` passes. `teams-step.component.spec.ts` fails 4 tests on master too (its mock state lacks `applyTeamsMetadata`) — not this change.

## Revision: one-club-rep-per-event rule removed (AR-142, Todd 2026-10-06)
- `RegisterTeamForEventAsync` no longer refuses a team because another rep of the club has teams in the event. Legacy never had the rule (added here 2026-01-04, `ef1a6e32c`), and it matched loosely: anyone ALSO a rep of the club counted, so a person repping two clubs blocked the other club's reps.
- Removed with it: `GET team-registration/check-existing` (no UI caller), `CheckExistingRegistrationsResponse`, `ITeamRepository.GetTeamsByClubExcludingRegistrationAsync`.
- New sign-ups never put two reps on one club (sign-up claims only a rep-less, team-less club). Older shared clubs exist, but in currently open events only ONE has two reps who both have teams (South Jersey Select, Five Star 2026); their reps now register side by side like same-name reps. (A raw count of clubs with 2+ rep rows is meaningless: it counts one person repping two clubs, and reps with no teams.)
- Left as is (Todd): the two API-only add-club endpoints; library "also rename in this event" doesn't check the event team is the caller's (no UI sends it).
- Recommendation 2 (Search flag) is met by the CADT "⚠ N reps" badge (Todd).
- Tester-facing summary: `scripts/Ann-Backend-Testing/AR-142-Club-Rep-Same-Name-Clubs.html`.
- Verified: backend 1240/1240; `npm run build` passes the gates.
