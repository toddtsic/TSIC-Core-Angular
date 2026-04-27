# Configure Menus - Punch List

**Tester:** Ann
**Date Started:** 2026-04-09
**Status:** In Progress

---

## How to Read Severity

| Label | Meaning |
|-------|---------|
| Bug | Something is broken or produces wrong results |
| UX | It works but is confusing, ugly, or hard to use |
| Question | Not sure if this is right -- need to ask Todd |

## How to Read Status

| Label | Meaning |
|-------|---------|
| Open | Not yet looked at |
| Fixed | Todd/Claude fixed it |
| Won't Fix | Intentional behavior, not changing |

---

## Test Areas

Use these as a guide for what to walk through. You don't have to go in order.

- [ ] **Menu Editor** -- Adding, editing, removing, and reordering menu items
- [ ] **Menu Display** -- How menus appear to different roles and on different screens
- [ ] **Menu Permissions** -- Role-based visibility and access control for menu items
- [ ] **Navigation** -- Links work correctly, proper routing, no dead ends
- [ ] **Mobile Menus** -- Responsive behavior, hamburger menu, touch targets

---

## Punch List Items

### PL-069: Job Settings / Mobile — confirm Store Contact Email is single-recipient; rename + tip if multi
- **Refs**: PL-049/PL-050 (Comms tab CC/BCC/Reschedule/Always-Copy email list pattern with `(semi-colon between emails, no spaces)` tip)
- **Area**: Menu Display / Job Settings → Mobile & Store tab → Store Settings
- **What I did**: Asked whether Store Contact Email accepts a single email or a list
- **What I expected**: Confirmation, then either keep single or rename "Store Contact Email List" with the Comms tip
- **What happened**: Currently single-email by both technique and intent — but worth confirming the desired behavior
- **Severity**: Question
- **Status**: Open
- **Note**: Findings:
  - **HTML technique**: `<input type="email">` ([mobile-store-tab.component.html:109](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/mobile-store-tab.component.html#L109)) — browser validator rejects semicolon-separated lists; only accepts a single valid email.
  - **Legacy usage**: stored as `ViewBag.cStoreContactEmail` in `StoreFamilyController.cs:193` and shown to customers as the "contact us" display value — not used as an email recipient list.
  - **New codebase**: no consumer of `storeContactEmail` for any automated send found; appears informational only.
  - **Two paths**:
    - **A. Keep single** — status quo, label stays "Store Contact Email", `type="email"` validation. Honest about the field's purpose (one canonical contact).
    - **B. Make multi-email** — change `type="email"` → `type="text"`, rename to "Store Contact Email List", add tip `(semi-colon between emails, no spaces)`. **Plus**: update display surface that renders the email to handle multiple (show all? show first? "Contact: A or B"?).
  - **Recommendation**: A. Customers who need multi-recipient contact can point this field at a distribution list address (e.g., `store@customer.com` → routes to multiple inboxes externally). Keeps TSIC out of the multi-recipient display logic.
  - **Decision for Todd**: confirm A (single) is correct, or call out a use case that requires B.

### PL-068: Job Settings / Mobile — Store Refund Policy and Store Pickup Details textareas need more height
- **Refs**: PL-050 (Branding overlay textareas bumped from `rows="2"` to `rows="4"` — same pattern)
- **Area**: Menu Display / Job Settings → Mobile & Store tab → Store Settings (SuperUser-only block)
- **What I did**: Reviewed the Store Refund Policy and Store Pickup Details textareas
- **What I expected**: Enough vertical space for Directors/SuperUsers to read multi-line policy text without scrolling within the field
- **What happened**: Both textareas use `rows="2"` ([mobile-store-tab.component.html:115, 121](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/mobile-store-tab.component.html#L115)) — too cramped for the typical content these fields hold (refund policy paragraphs, pickup details with location + hours)
- **Severity**: UX
- **Status**: Open
- **Note**: Bump both to `rows="4"`. UI-only; no DB/DTO/service change. Same fix shape as PL-050 applied to the overlay headline/subheadline textareas. With the global `.field-input` line-height: 1.2 from the Branding session already in place, four rows reads as a tight, generous block.

### PL-067: Job Settings / Mobile — break Mobile Features into TSIC-Events and TSIC-Teams subsections
- **Refs**: PL-066 (Push Directors potentially landing on Mobile if restored — affects subsection placement)
- **Area**: Menu Display / Job Settings → Mobile & Store tab
- **What I did**: Reviewed the flat single-block Mobile Features section
- **What I expected**: Two subsections (TSIC-Events and TSIC-Teams), each with its own Enabled master toggle, with related per-feature toggles grouped under one or the other (or shared)
- **What happened**: All seven Director-visible Mobile fields render in one undifferentiated block, making it unclear which toggles affect which app
- **Severity**: UX
- **Status**: Open — Todd discussion needed for the field-to-subsection mapping
- **Note**: Current Director-visible Mobile fields:
  - `tsicEventsEnabled` (inverse of `bSuspendPublic`) — TSIC-Events master enable
  - `bEnableTsicteams` — TSIC-Teams master enable
  - `bEnableMobileRsvp` — Mobile RSVP
  - `bEnableMobileTeamChat` — Mobile Team Chat
  - `bAllowMobileLogin` — allow mobile login
  - `bAllowMobileRegn` — allow mobile registration
  - `mobileScoreHoursPastGameEligible` — score eligibility window (hours past game)
  - **Suggested grouping (starting point — Todd to confirm against actual app consumption)**:
    | Subsection | Master toggle | Sub-toggles |
    |---|---|---|
    | **TSIC-Events** | `tsicEventsEnabled` | `mobileScoreHoursPastGameEligible` (game scoring is events) |
    | **TSIC-Teams** | `bEnableTsicteams` | `bEnableMobileRsvp` (RSVP = team event), `bEnableMobileTeamChat` (chat = team) |
    | **Cross-cutting** | — | `bAllowMobileLogin`, `bAllowMobileRegn` (gate functionality across both) |
  - **UI shape**: render as two distinct cards/fieldsets within the Mobile Features section. Each has its master toggle at the top; sub-toggles disable when master is off (parent-child toggle pattern, similar to how `bRegistrationAllowPlayer` parents `bPlayerRegRequiresToken` on the Player tab). Cross-cutting toggles in their own card below.
  - **Todd decision points**:
    1. Confirm the field-to-subsection mapping against actual app consumption (which flag does the Events app vs Teams app actually read).
    2. Should cross-cutting toggles render in their own "Mobile App Access" subsection, or duplicate into both Events/Teams cards?
    3. Master-toggle behavior: when the master is OFF, do sub-toggles auto-disable in the UI only, or should saving with the master OFF also clear the sub-flags in DB?
    4. SuperUser-only Store fields stay on the same tab but in their existing dedicated section — no change needed there.

### PL-066: "Push Directors" toggle — vestigial today; decide remove vs restore-and-move-to-Mobile
- **Refs**: PL-061 (zombie-flag audit; currently recommends "remove" for `bTeamPushDirectors`); Legacy placement was on the Mobile tab
- **Area**: Menu Display / Job Settings → Teams tab (current) → Mobile tab (Legacy / proposed if restored)
- **What I did**: Reviewed where the "Push Directors" toggle should live — Legacy had it on Mobile, new version has it on Teams
- **What I expected**: Move to Mobile to match Legacy
- **What happened**: Confirmed `BTeamPushDirectors` is **vestigial** in the new codebase (no push-notification consumer). Push infrastructure exists (FirebaseMessaging, FcmToken in `TSIC.API/Program.cs` and `AspNetUsers` entity) but doesn't read this flag. So "move to Mobile" only makes sense if the underlying feature is restored.
- **Severity**: Question (feature scope) + Cleanup
- **Status**: Open
- **Note**: Three paths:
  - **A. Remove entirely** (PL-061's current recommendation) — honest about reality; toggle does nothing today; UI/DTO/service strip, keep DB column.
  - **B. Restore + move to Mobile** — reimplement the push-notification logic in `TeamRegistrationService` (fire a Firebase push to job Directors when a team registers), then move the toggle to the Mobile tab where Director-facing push controls belong. Legacy parity at the cost of new code; needs Director FCM-token capture confirmed.
  - **C. Move to Mobile but stay vestigial** — relocates a zombie; worst of both worlds.
  - **Recommendation**: A unless Todd confirms the push-Directors feature is still desired. If desired, B (with an explicit feature-rebuild PR, not just a UI move). C is not recommended.
  - **Decision sequence**: Todd answers "is push-on-team-registration to Directors a feature TSIC wants?" → if yes, plan the B work; if no, ship A as part of PL-061's atomic cleanup.

### PL-065: Job Settings — move "Show Team Name Only in Schedules" from Teams tab to Scheduling tab (Legacy parity)
- **Refs**: PL-061 (Team Options audit confirmed this is the one functional flag of four — needs help copy + placement decision)
- **Area**: Menu Display / Job Settings → Teams tab (current) → Scheduling tab (proposed)
- **What I did**: Compared placement of `bShowTeamNameOnlyInSchedules` in Legacy vs new
- **What I expected**: Settings consumed by schedule rendering to live on the Scheduling tab where Directors naturally configure schedule appearance
- **What happened**: Currently lives on Teams tab; in Legacy it was under Scheduling
- **Severity**: UX
- **Status**: Open
- **Note**: Three reasons to move:
  1. **Behavior consumption point**: the flag drives schedule rendering at [ScheduleRepository.cs:42-47](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/ScheduleRepository.cs#L42-L47) — the visible effect happens when schedules render, not when teams are managed.
  2. **Legacy parity**: Directors who learned the old layout will look in Scheduling first.
  3. **Conceptual fit**: it's a display setting (how the team name appears on schedules), not a team property.
  - **Three placement options**:
    - **A. Move to Scheduling tab** — Legacy parity, conceptual fit. **Recommended.**
    - **B. Keep on Teams tab** — current state; reflects "team-name-related" framing.
    - **C. Both / cross-link** — render on Scheduling but expose a read-only mirror or link on Teams. Adds chrome without resolving where Directors actually edit.
  - **Implementation (option A)**:
    - Frontend: move the field signal from `teams-tab.component.ts:25` → `scheduling-tab.component.ts`; remove from teams-tab; add to scheduling-tab template.
    - DTO: move `bShowTeamNameOnlyInSchedules` from `JobConfigTeamsDto` → `JobConfigSchedulingDto`.
    - Service: `JobConfigService.UpdateSchedulingAsync` sets `Jobs.BShowTeamNameOnlyInSchedules`; strip from `UpdateTeamsAsync`.
    - DB column unchanged.
  - **Bundle with PL-061** since both deal with the same Teams Options cluster — atomic change for the audit + relocation.

### PL-064: Job Settings / Coaches — Referee & Recruiter sections: confirm support, audit usage
- **Refs**: PL-044 (Refund Policy removal from Coaches tab — separate item, already covered)
- **Area**: Menu Display / Job Settings → Coaches tab
- **What I did**: Reviewed the Referee Confirmations and Recruiter Confirmations sections on the Coaches tab and asked whether these registration types are still supported
- **What I expected**: Confirmation that both roles are active (or removable as Legacy artifacts)
- **What happened**: Both are **active registration types** with per-role fallback in the runtime — but it's worth deciding whether TSIC still onboards customers using them
- **Severity**: Question
- **Status**: Open — Todd discussion
- **Note**: Findings:
  - **Referee Confirmations** section at [coaches-tab.component.html:74](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L74): Confirmation Email + On-Screen RTE editors backed by `refereeRegConfirmationEmail` / `refereeRegConfirmationOnScreen`.
  - **Recruiter Confirmations** section at [coaches-tab.component.html:100](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L100): same shape, backed by `recruiterRegConfirmationEmail` / `recruiterRegConfirmationOnScreen`.
  - **Backend routes per role** with fallback at [AdultRegistrationService.cs:1319-1320](TSIC-Core-Angular/src/backend/TSIC.API/Services/Adults/AdultRegistrationService.cs#L1319-L1320):
    ```csharp
    AdultRoleType.Referee => job.RefereeRegConfirmationOnScreen ?? job.AdultRegConfirmationOnScreen,
    AdultRoleType.Recruiter => job.RecruiterRegConfirmationOnScreen ?? job.AdultRegConfirmationOnScreen,
    ```
    If the role-specific text is set, that's used; otherwise fall back to generic Adult confirmation.
  - **Referee role has additional infrastructure**: `RefAssignmentService.cs` exists for referee assignment workflow — Referee isn't just a registration role, it's a feature with assignment logic.
  - **Recruiter role**: only the confirmation routing was found in this scan; no equivalent assignment service. May be lighter-weight feature.
  - **Decision points for Todd**:
    1. **Are TSIC customers actively using Referee registration?** If yes, keep section as-is. If no, remove the two fields + section (keep DB columns for legacy data).
    2. **Are TSIC customers actively using Recruiter registration?** Same question.
    3. **Coaches tab structure**: if both stay, the tab covers Coach + Staff + Referee + Recruiter — possibly worth renaming the tab to "Coaches & Staff" (matches existing service-tier label at `job-config.service.ts:220`) or even "Adult Registrations" to capture the breadth.
  - **Recommendation going in**: keep both unless Todd confirms zero customer usage, since they're functional and have working fallback semantics. UX polish (icons/help text on the sections) is low-priority versus the supported-vs-removed decision.

### PL-063: Job Settings — gate all four "regform name" fields to SuperUser only (Legacy parity)
- **Refs**: PL-053 (Registration Form `coreRegformPlayer` SuperUser-only — same theme, separate field)
- **Area**: Menu Display / Job Settings → Player tab + Teams tab + Coaches tab
- **What I did**: Identified four "Form Name" / regform-name fields exposed to Directors that were SuperUser-only in Legacy
- **What I expected**: All four locked down to SuperUser, matching Legacy
- **What happened**: All four render unconditionally on their respective tabs — Director can edit any of them today
- **Severity**: Bug (role visibility)
- **Status**: Open
- **Note**: These are cosmetic admin-metadata labels for the underlying regforms — not the core regform identifier (PL-053 covers that separately for `coreRegformPlayer`). Four fields to gate together:
  | Tab | Field | Current location |
  |---|---|---|
  | Player | `regformNamePlayer` | (renders unconditionally — saw earlier in PL-053 review; not part of PL-053's named scope) |
  | Teams | `regformNameTeam` | unconditionally rendered |
  | Teams | `regformNameClubRep` | unconditionally rendered |
  | Coaches | `regformNameCoach` | [coaches-tab.component.html:8-12](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L8-L12) — flagged this pass |
  - **Frontend fix**: wrap each field in `@if (svc.isSuperUser())`, OR move into the SuperUser-only block on each tab where one already exists. Mirror PL-053's approach.
  - **Backend hardening**: `JobConfigService.UpdatePlayerAsync` / `UpdateTeamsAsync` / `UpdateCoachesAsync` must reject Director attempts to set these four properties — guard each in an `if (isSuperUser) { ... }` block, same pattern General uses for its SuperUser-only fields.
  - **Bundle with PL-053** when shipping — same atomic change makes sense since both are role-gating cleanups on Player/Teams/Coaches tabs.

### PL-062: Job Settings — clarify Club Rep confirmation text (currently shares Coaches templates via the Adult flow)
- **Refs**: PL-058 (Roster Visibility move per role), PL-053 (role-targeted-content placement principle)
- **Area**: Menu Display / Job Settings → Coaches tab + Club Reps/Teams tab; downstream team registration confirmation
- **What I did**: Asked whether Club Reps should have their own Confirmation Text editor, or whether confirmation is hardcoded
- **What I expected**: Answer + recommendation on per-role confirmation text vs. shared
- **What happened**: Confirmed Team Registration uses `AdultRegConfirmationEmail` + `AdultRegConfirmationOnScreen` — the **same templates** edited on the Coaches tab. Not hardcoded; not duplicated; shared with Coaches via the Adult flow.
- **Severity**: Question
- **Status**: Open — Todd discussion
- **Note**: Architecture today:
  - **Schema**: only one set of Adult confirmation fields exists — `AdultRegConfirmationEmail` and `AdultRegConfirmationOnScreen` on the Jobs entity, edited on the Coaches tab.
  - **Send pathway**: `TeamRegistrationService.SendClubRepConfirmationEmailAsync` (line 1408) reads `AdultRegConfirmationEmail`. The on-screen confirmation similarly reads `AdultRegConfirmationOnScreen`.
  - **Frontend confirms**: comment in [team-registration.service.ts:208-210](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/services/team-registration.service.ts#L208) — *"Send confirmation email to club rep with substituted template. Uses AdultRegConfirmationEmail template from the Job"*.
  - **The trade-off**: TSIC's "Adult" registration flow lumps Coaches, Staff, AND Club Reps together. Whatever the Director writes for the Adult confirmation reaches all three roles — so wording like "Welcome, Coach!" lands on a Club Rep registering teams.
  - **Options**:
    - **A. Status quo (keep shared)** — one Adult confirmation template covers all three roles. Simple; relies on neutral wording. No schema change.
    - **B. Add separate Club Rep templates** — new fields `clubRepRegConfirmationEmail` / `clubRepRegConfirmationOnScreen`; new Confirmation Text card on the Club Reps/Teams tab; `TeamRegistrationService` switches to these. Cleanest UX per role; schema additions; clone path to update.
    - **C. Single template + `!ROLE` token** — keep one Adult template; introduce `!ROLE` substitution token resolving to "Coach"/"Staff"/"Club Rep" at send time. Director writes once, gets correct role wording per recipient. Lightweight; matches TSIC's existing `!`-token pattern (e.g., `!INVITE_LINK`).
  - **Recommendation**: **C** — keeps the architecture simple, removes the shared-text problem, fits the existing token convention. **B** if Todd wants Club Reps to have content distinct enough that token substitution isn't sufficient (e.g., team-management instructions that don't apply to Coaches).
  - **Decision points for Todd**: (1) is the current Adult-flow shared-template behavior intentional or a Legacy quirk to fix? (2) if separating, is full text per role (B) or token substitution (C) the right path?

### PL-061: Job Settings / Teams — audit four "Team Options" toggles: 2 vestigial, 1 misplaced, 1 needs explanation
- **Refs**: PL-046 (other zombie flags Refunds-in-Prior-Months / Allow-Credit-All — same vestigial-flag pattern); PL-008 (teamEligibilityByAge flag drives Age Ranges menu visibility)
- **Area**: Menu Display / Job Settings → Teams tab; downstream player registration + scheduling
- **What I did**: Reviewed the four "Team Options" toggles — Restrict Players to Age Range, Use Waitlists, Push Directors, Show Team Name Only in Schedules — and traced each to find consumers
- **What I expected**: Each toggle to either drive real behavior or be removed; placement to match where the behavior actually fires
- **What happened**: Two are vestigial, one is misplaced (lives on Teams tab but only affects Players), one is functional but needs Director-facing copy
- **Severity**: Bug (zombie settings) + UX
- **Status**: Open
- **Note**: Per-toggle findings:
  - **`bRestrictPlayerTeamsToAgerange`** ("Restrict Players to Age Range") — **vestigial**. Only appears in CRUD plumbing (DTO, service, clone, entity); no runtime consumer in the new codebase. Legacy uses it in `PlayerBaseController.cs`. Plus: per Ann's note, this is a Player-tab concern conceptually — and tied to PL-008 (Age Ranges menu visibility, which is gated by the `teamEligibilityByAge` flag derived from `CoreRegformPlayer == 'BYAGERANGE'`). **Recommendation**: remove from UI/DTOs (keep DB column for legacy data). If revived in the future, it belongs on the Player tab and should ride the same `teamEligibilityByAge` flag as the Age Ranges menu.
  - **`bUseWaitlists`** ("Use Waitlists") — **misplaced + likely vestigial for both flows**. Code at [TeamPlacementService.cs:71](TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamPlacementService.cs#L71) explicitly comments: *"BUseWaitlists is a player-registration-only flag and is NOT checked here. Team registration always supports waitlists (driven by MaxTeams per agegroup)."* So for **teams**, the flag is irrelevant — waitlists are always created when agegroup hits max (test "BUseWaitlists OFF → still creates waitlist (teams always waitlist)" confirms). For **players**, I couldn't find any consumer either — likely also vestigial, needs deeper grep against the player registration flow. **Recommendation**: remove from Teams tab regardless. If a player consumer is found, move to Player tab; if not, full removal (keep DB column).
  - **`bTeamPushDirectors`** ("Push Directors") — **vestigial**. Only CRUD plumbing in the new codebase; no consumer. Legacy has it. **Recommendation**: remove from UI/DTOs (keep DB column).
  - **`bShowTeamNameOnlyInSchedules`** — **functional**, drives team-name display on schedules at [ScheduleRepository.cs:42-47](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/ScheduleRepository.cs#L42-L47). When true, shows just the team name on schedules; when false, presumably shows fuller club+team composition. **Action needed**: keep the toggle, add Director-facing help text explaining both states (e.g., *"When enabled, schedules show just the team name. When disabled, schedules show the full club + team composition."*). May also want to confirm with Todd what the OFF state actually displays.
  - **Decision sequence**:
    1. Confirm `bRestrictPlayerTeamsToAgerange` and `bTeamPushDirectors` are removable (no hidden consumers Todd remembers).
    2. Trace `bUseWaitlists` against player registration code to confirm vestigial there too — if not, move to Player tab.
    3. Add help copy to `bShowTeamNameOnlyInSchedules` after confirming the OFF-state display format with Todd.
    4. Strip three toggles from UI/DTO/service/clone in one atomic change; keep DB columns.

### PL-060: Job Settings / Teams — Club Rep permissions need post-registration auto-lock or time-based gating
- **Area**: Menu Display / Job Settings → Teams tab; Teams Library; Team Registration flow
- **What I did**: Reviewed how `bClubRepAllowEdit` / `bClubRepAllowDelete` / `bClubRepAllowAdd` are used and asked how Directors prevent Club Reps from changing teams (especially deleting them) **after** registration closes
- **What I expected**: Some kind of automatic transition — either job-status driven (when reg closes, edit/delete locks) or time-based (toggle expires on a specified date)
- **What happened**: All three flags are job-wide booleans with no time/status awareness. Set once; they apply forever until manually flipped. If a Director enables Edit/Delete during registration and forgets to flip them off post-close, Club Reps retain those permissions indefinitely.
- **Severity**: Bug (operational risk) / Feature
- **Status**: Open
- **Note**: Confirmed how the flags work today:
  - **Server-side enforcement**: `TeamRegistrationService` checks `capabilities.ClubRepAllowAdd` ([line 571](TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamRegistrationService.cs#L571)) and `capabilities.ClubRepAllowDelete` ([line 784](TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamRegistrationService.cs#L784)) before allowing Add/Delete; refuses with a clear error message if false.
  - **Single source of truth**: both the Team Registration screen and the Teams Library go through the same backend gate, so the toggles aren't duplicated — moving them to either screen is a UX choice, not a behavior gap.
  - **The gap**: no automatic post-registration lockdown. Director must manually disable Edit/Delete after reg closes, or Club Reps keep those permissions.
  - **Fix options**:
    - **A. Auto-lock on registration close** — when `capabilities.TeamRegistrationOpen = false`, deny Edit/Delete regardless of the flags. Default-safe; removes manual step. Loses flexibility for post-reg cleanup edits.
    - **B. New "post-close" permission set** — `bClubRepAllowEditAfterRegClose` / `bClubRepAllowDeleteAfterRegClose` flags that take effect once registration closes. Most flexible; most schema sprawl.
    - **C. Status quo + better copy + reminder** — keep manual toggles, add help text on the field labels, optionally fire a reminder email to Director when registration window is about to close ("Don't forget to disable Club Rep Edit/Delete if you want to lock teams").
    - **D. Replace booleans with date fields** — "Allow Club Rep Edit until [date]" — auto-locks at midnight on the date. Self-documenting; no Director memory required. Matches how registration windows themselves are date-driven.
  - **Recommendation**: D is the most TSIC-idiomatic (registration deadlines are already date-driven elsewhere), but it's a schema change. A is the quickest-and-safest default. Todd to decide.

### PL-059: Job Settings / Teams — rename tab from "Teams" to "Club Reps/Teams"
- **Area**: Menu Display / Job Settings → tab bar
- **What I did**: Reviewed the tab label for the team configuration area
- **What I expected**: Tab name to surface that it covers both Club Reps and Teams (sibling tab "Coaches & Staff" already does this for two-role pairings)
- **What happened**: Tab label is just "Teams" ([job-config.component.ts:54](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.component.ts#L54)); the longer-form label used in dirty-state warnings is "Teams & Club Reps" ([job-config.service.ts:219](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.service.ts#L219)) — already inconsistent
- **Severity**: UX
- **Status**: Open
- **Note**: Three options (target wording: **"Club Reps/Teams"** — both plural, slash separator):
  - **A. Tab only** — change tab to "Club Reps/Teams"; leave service label "Teams & Club Reps" untouched. Matches literal ask but preserves existing inconsistency.
  - **B. Unify on "Club Reps/Teams"** — tab and service label both use the same slash format. Compact for tab bar.
  - **C. Unify on "Club Reps & Teams"** — matches sibling tab `'Coaches & Staff'` ampersand convention; preserves "Club Reps first" word order. Most consistent across the tab family.
  - **Recommendation**: B — matches Ann's exact wording everywhere; "/" reads tighter in the tab bar than "&"; minor tab-family inconsistency with "Coaches & Staff" is tolerable. UI-only, no DB/DTO/service touch.

### PL-058: Job Settings — relocate Roster Visibility checkboxes to Player and Coaches tabs; add explanatory copy
- **Refs**: PL-053 (Player-tab field gating discussion); same role-targeted-content placement principle
- **Area**: Menu Display / Job Settings → Teams tab (current location) → Player + Coaches tabs (proposed)
- **What I did**: Found the Roster Visibility section on the Teams tab with two checkboxes labeled simply "Adult" and "Player"
- **What I expected**: Each role-gating checkbox to live with its role's settings (Player tab and Coaches tab), with clear language explaining what each toggle allows
- **What happened**: Both currently live on the Teams tab ([teams-tab.component.html:98-113](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/teams-tab.component.html#L98-L113)) under "Roster Visibility", with one-word labels ("Adult" / "Player") and no help text
- **Severity**: UX
- **Status**: Open — Todd discussion needed
- **Note**: Two-part change:
  1. **Move per role**:
    - `bAllowRosterViewPlayer` → Player tab (next to other player-facing settings)
    - `bAllowRosterViewAdult` → Coaches tab (next to other coach/club-rep settings)
    - DB columns (`Jobs.BAllowRosterViewPlayer`, `Jobs.BAllowRosterViewAdult`) stay put — only DTO/service mapping shifts.
    - DTO: move `bAllowRosterViewPlayer` from `JobConfigTeamsDto` → `JobConfigPlayerDto`; `bAllowRosterViewAdult` from `JobConfigTeamsDto` → `JobConfigCoachesDto`.
    - Service: `JobConfigService.UpdatePlayerAsync` sets `Jobs.BAllowRosterViewPlayer`; `UpdateCoachesAsync` sets `Jobs.BAllowRosterViewAdult`. Strip from `UpdateTeamsAsync`.
    - Runtime gate (`MyRosterService.cs:42-43`) is unaffected — still reads from the same DB columns.
  2. **Clarify language**: replace the bare "Adult" / "Player" labels with self-explanatory copy, e.g.:
    - Player tab: **"Allow players to view their team roster"** with help: *"When enabled, registered players can see their teammates' names on their team page."*
    - Coaches tab: **"Allow coaches & staff to view their team roster"** with help: *"When enabled, coaches and club reps can see all rostered players on their team page."*
  - **Decision points for Todd**:
    1. Confirm move per role is the right placement (vs. keep on Teams as a "team visibility" cluster).
    2. Confirm the proposed labels match TSIC's terminology.
    3. Audit whether any other fields on the Teams tab make more sense on a role-specific tab (e.g., `bShowTeamNameOnlyInSchedules` — affects what every viewer sees, probably stays on Teams; `bUseWaitlists` — Player or Coaches?).

### PL-057: Job Settings / Player + Coaches — normalize oversized text in migrated Legacy RTE content
- **Area**: Menu Display / Job Settings → Player tab + Coaches tab; downstream registration flows
- **What I did**: Reviewed Release of Liability content (and similar) where headings render dramatically larger than body text — Legacy migration carried over `<h1>`/`<h2>`/`font-size` styles
- **What I expected**: Body and headings in the same text box to render at consistent text size
- **What happened**: Migrated content has heading tags baked in; new RTE toolbar already prevents new heading additions, but existing data renders oversized
- **Severity**: UX / Data cleanup
- **Status**: Open
- **Note**:
  - **Toolbar status (good news)**: [rte-config.ts:2-9](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/shared/rte-config.ts#L2-L9) already restricts to Bold/Italic/Underline/FontColor/BG/Lists/Link — no heading dropdown, no font-size picker. Future content stays clean automatically. Issue is only legacy migrated rows.
  - **Affected fields** (10 RTE editors per job × all jobs):
    - Player tab: `playerRegConfirmationEmail`, `playerRegConfirmationOnScreen`, `playerRegRefundPolicy`, `playerRegReleaseOfLiability`, `playerRegCodeOfConduct`, `playerRegCovid19Waiver` (being removed per PL-055)
    - Coaches tab: `adultRegConfirmationEmail`, `adultRegConfirmationOnScreen`, `adultRegRefundPolicy`, `adultRegReleaseOfLiability`, `adultRegCodeOfConduct`
  - **Fix options**:
    - **A. One-time SQL cleanup** — strip `<h1>`–`<h6>` tags and inline `font-size:` styles across all affected rows. Replacement choice: `<strong>` (preserves emphasis) vs `<p>` (full normalization) for what was a heading. Permanent; touches data only.
    - **B. Display-time sanitize pipe** — strip oversized formatting on render in the player/adult registration flows. Source preserved; registration display always reads clean.
    - **C. Both A and B** — A so Directors see clean source when editing; B as a safety net for any future bad content.
  - **Recommendation**: A — RTE toolbar already prevents new bad content, so a one-time data fix is sufficient. B is overkill if the toolbar holds. Run cleanup as a script in `scripts/` so it's auditable + repeatable.
  - **Sample SQL approach** (rough draft, validate before running):
    ```sql
    UPDATE Jobs.Jobs SET
      PlayerRegReleaseOfLiability = <strip-headings-fn>(PlayerRegReleaseOfLiability),
      ... (all 10 fields)
    WHERE PlayerRegReleaseOfLiability LIKE '%<h%' OR ...
    ```
    Better implemented in C# as a one-time migration that loads each row, runs `Regex.Replace` to strip heading tags + font-size styles, writes back. Safer than raw SQL pattern matching.
  - **Decision points for Todd**: confirm replacement strategy (`<strong>` vs `<p>`), confirm scope is just these 10 fields (or also bulletins, banners, other RTE content?), confirm OK to rewrite all jobs in one pass.

### PL-055: Job Settings / Player — remove COVID-19 Waiver field (no longer needed)
- **Refs**: PL-044 (Refund Policy moves to Payment header — same Player-tab cleanup pass); PL-053 (Mom/Dad Label removal — same "keep DB column, strip UI/API" pattern)
- **Area**: Menu Display / Job Settings → Player tab + downstream waiver flow
- **What I did**: Identified the COVID-19 Waiver editor field on the Player tab as no longer relevant
- **What I expected**: Cleanup of the field across the editor, registration wizard, and email substitution
- **What happened**: Field is still wired across multiple layers — needs a sweep, not just a UI delete
- **Severity**: Cleanup
- **Status**: Open
- **Note**: COVID-19 Waiver footprint:
  - **Editor UI**: Player tab textarea (RTE) — strip from `player-tab.component.html` and `player-tab.component.ts`
  - **DTOs**: drop `playerRegCovid19Waiver` from `JobConfigPlayerDto` and `UpdateJobConfigPlayerRequest`
  - **Service**: `JobConfigService.UpdatePlayerAsync` — drop the field assignment + mapping
  - **Clone**: `JobCloneService` — drop the carry-forward line
  - **Repository**: `JobRepository` — drop from `JobMetadataResponse` mapping
  - **Registration wizard**: `waiver-state.service.ts` (player registration flow) — drop the COVID step from the waiver chain
  - **Email substitution token**: `!F-COVID-WAIVER-PLAYER` registered at [TextSubstitutionService.cs:397-398](TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/TextSubstitution/TextSubstitutionService.cs#L397-L398) — strip the registration. **Audit any email templates** (confirmation emails, batch emails) that still reference the token; replace with nothing or remove the surrounding block.
  - **DB column**: keep `Jobs.PlayerRegCovid19Waiver` intact to preserve historical waiver text from past seasons — drop UI/API only. Same pattern as Mom/Dad Label removal in PL-053.
  - **Sequence**: (1) backend DTO/service/clone/repo strip together (atomic API change), (2) regen frontend models, (3) frontend UI strip + waiver-state strip, (4) email template audit + token registration removal. One PR; lots of touchpoints.

### PL-054: Job Settings / Player — review "Require Invitation Token" function with Todd; decide if SuperUser-only
- **Refs**: PL-053 (companion role-gating decisions on the Player tab)
- **Area**: Menu Display / Job Settings → Player tab
- **What I did**: Reviewed the "Require Invitation Token" toggle's function and asked whether it should be locked to SuperUser
- **What I expected**: Confirmation of what the flag does, then decision on visibility
- **What happened**: Function confirmed real and active; visibility decision pending Todd review
- **Severity**: Question
- **Status**: Open
- **Note**: Findings from walkthrough on 2026-04-25:
  - **Function**: `bPlayerRegRequiresToken = true` → players must use a valid invite link to register; `false` → open registration. Drives [PlayerInviteController.ValidateInvite](TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerInviteController.cs#L48-L50) at the API gate.
  - **Current UI** ([player-tab.component.html:22-32](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L22-L32)): renders for all roles, paired with "Allow Player Registration" parent toggle, has a clear tooltip explaining both states.
  - **Companion**: `BTeamRegRequiresToken` exists on the Teams tab — same pattern. Whatever decision is made here should likely apply to that one too for consistency.
  - **Decision points for Todd**:
    1. Confirm function still works as designed (especially the `!INVITE_LINK` substitution token referenced in the tooltip).
    2. Should this be **Director-controlled** (operational per-season decision) or **SuperUser-only** (registration-mode change with downstream impact)?
       - **Director-controlled**: matches current behavior; Directors flip between invite-only and open seasons themselves. Needs the existing tooltip to be sufficient guardrail.
       - **SuperUser-only**: Director flips registration on/off via "Allow Player Registration"; SuperUser controls the registration mode (open vs invite-only). Locks down a flag that affects the API pathway.
    3. Apply same decision to `BTeamRegRequiresToken` on Teams tab so Player and Team flows stay aligned.
  - **Recommendation going in**: Director-controlled — the tooltip already documents both states and it's a per-season operational choice. But Todd may have history with Directors mis-toggling this that argues for SuperUser-only.

### PL-053: Job Settings / Player — move Registration Form, Multi-Player Min, Discount % to SuperUser-only; verify RegSaver placement
- **Refs**: PL-039 (Branding tab SuperUser-only), PL-041 (Per-Unit Charges SuperUser-only), PL-043 (Processing Fee SuperUser-only) — same pattern across tabs
- **Area**: Menu Display / Job Settings → Player tab
- **What I did**: Reviewed which Player tab fields should be SuperUser-only
- **What I expected**: Platform-level fields (form selection, multi-player discount math, insurance offer) gated to SuperUser; Director-level fields (waivers, confirmation copy) stay open
- **What happened**: Three platform-level fields render for all roles today — only RegSaver Insurance + Mom/Dad Label are correctly SuperUser-gated
- **Severity**: Bug (role visibility) + Question (function review)
- **Status**: Open
- **Note**:
  - **Move into existing SuperUser block** ([player-tab.component.html:138-152](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L138-L152)):
    1. **Registration Form** (`coreRegformPlayer`) at [lines 46-50](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L46-L50) — Directors shouldn't pick which regform their job uses; that's a platform/profile decision.
    2. **Multi-Player Min** (`playerRegMultiPlayerDiscountMin`) at [lines 51-56](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L51-L56) — needs Todd review of function (does it actually drive the discount? trigger threshold semantics?) before locking.
    3. **Discount %** (`playerRegMultiPlayerDiscountPercent`) at [lines 57-62](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L57-L62) — same — confirm the field actually applies the percent at registration time.
  - **Already correctly placed** (no action needed, just confirming): **Offer RegSaver Insurance** lives inside the existing `@if (svc.isSuperUser())` block at [line 138](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L138). Stay put.
  - **Remove entirely** — **Mom Label** (`momLabel`) and **Dad Label** (`dadLabel`) at [lines 153-160+](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L153) are no longer relevant. Strip from UI, DTO, service, and `UpdateJobConfigPlayerRequest`. Keep DB columns (`Jobs.momLabel` / `Jobs.dadLabel`) intact to preserve historical data — drop UI/API only. Anywhere else in the codebase still consumes them (player registration form labels, confirmation emails, etc.) needs to be checked + replaced with hardcoded "Mom" / "Dad" or whatever the new convention is — sweep needed before stripping.
  - **Backend hardening required** (matches the pattern from PL-041/PL-043): `JobConfigService.UpdatePlayerAsync` (or equivalent) must reject Director attempts to set `coreRegformPlayer` / `playerRegMultiPlayerDiscountMin` / `playerRegMultiPlayerDiscountPercent` — wrap those assignments in an `if (isSuperUser) { ... }` guard like General does for its SuperUser-only fields.
  - **Decision sequence**: (1) Todd review of Multi-Player Min / Discount % function before moving (or dropping); (2) sweep momLabel/dadLabel consumers before stripping; (3) one atomic change for all the moves + removals + backend gate.

### PL-052: Two-banner architecture — `client-banner` widget vs `dashboard-hero` inline; standardize or keep distinct
- **Refs**: PL-040 (broader Branding ↔ Widget Editor architecture review); inline aspect-ratio fix shipped on `client-banner` 2026-04-25
- **Area**: Branding / widget-dashboard / client-banner widget
- **What I did**: Standardized `client-banner` on `aspect-ratio: 50 / 11` (TSIC-prepared 2000×440 source) on 2026-04-25 to fix the cropping issue Ann reported on Girls Summer Showcase 2026. While verifying, discovered logged-in users see a different, narrower banner that's untouched by the fix.
- **What I expected**: One banner system rendering the same Banner Background image consistently
- **What happened**: There are **two separate banner components** sharing the same `bannerBackgroundImage` source:
  - **`client-banner`** ([widgets/layout/client-banner/](TSIC-Core-Angular/src/frontend/tsic-app/src/app/widgets/layout/client-banner/)) — registered as a widget; renders on the public-facing site and on the Public View tab (when logged in) **only if a job's widget config places it**. Now uses `aspect-ratio: 50 / 11`, no height cap.
  - **`dashboard-hero`** (inline in [widget-dashboard.component.html:40-59](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/widget-dashboard/widget-dashboard.component.html#L40-L59)) — renders on the Director/SuperUser View tab; height driven by content + padding (~200-260px tall), image as `position: absolute` background with a dark gradient overlay for text readability. Explicitly **skips the client-banner widget** when in admin view (per [line 89](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/widget-dashboard/widget-dashboard.component.html#L89) comment "Skip client-banner (hero already renders it)").
- **Severity**: Question (architecture)
- **Status**: Open
- **Note**: Decision needed with Todd — three reasonable shapes:
  - **A. Keep them distinct** — admin view needs vertical density to surface dashboard content; public site benefits from a tall image-driven hero. Both are correct for their context. Document the split so future Branding work doesn't fight it.
  - **B. Standardize on the new aspect-ratio** — apply `aspect-ratio: 50 / 11` (or whatever TSIC's banner spec lands at) to `dashboard-hero` too. Pros: visual consistency, single banner spec for SuperUsers to design against. Cons: admin view loses screen real estate to a taller banner.
  - **C. Merge into one component** — one `JobBanner` everywhere with conditional inner content (admin context strip vs public hero text). Larger refactor, but kills the dual-source-of-truth and dovetails with PL-040's "review Branding architecture" thread.
  - **Recommendation going in**: B as the smallest move that fixes the visual inconsistency Ann discovered. C is the cleanest long-term answer if Todd has refactor appetite. A is fine if Todd believes admin vs public truly need different banner shapes.
  - **Tied to PL-039**: if Branding becomes SuperUser-only, the spec discipline gets easier — TSIC controls every banner shape. Worth deciding A/B/C alongside the SuperUser-only gate.
  - **Verification path**: to confirm the client-banner fix actually works on the public side, view the public-facing URL for Girls Summer Showcase 2026 (no login). Public View tab inside the dashboard only shows client-banner if it's placed in that job's widget config — absence there isn't the same as the fix being broken.

### PL-051: Accessibility — `<label class="field-label">` elements across Configure not linked to their inputs (WCAG / S6853)
- **Area**: Accessibility / shared `field-*` form classes across all Configure tabs
- **What I did**: Surfaced during PL-050 edits on the Comms tab — IDE reported 6 × `Web:S6853` warnings ("A form label must be associated with a control and have accessible text") on every `<label class="field-label">` on that file
- **What I expected**: Every label linked to its input either via `<label for="x">` + `<input id="x">` or by wrapping the input inside the label
- **What happened**: Pre-existing issue across the Configure surface — the shared `field-label` pattern renders labels as free-standing text with no association to the nearby input. Screen readers can't announce the label when the input gains focus; clicking the label doesn't focus the input.
- **Severity**: Accessibility (WCAG 1.3.1 / 3.3.2) / pre-existing
- **Status**: Open
- **Note**: Not caused by PL-050's edits — PL-050 just shifted line numbers so the linter re-reported them. The pattern affects six labels on the Comms tab alone and almost certainly every other tab in Job Configuration (General, Player, Teams, Coaches, Payment, Scheduling, Branding, Mobile & Store) plus any other `.field-label` usage across the app.
  - **Fix shape** — for each label/input pair:
    1. Add `id="<fieldName>"` to the input (most don't have one today).
    2. Add `for="<fieldName>"` to the label.
    3. Input IDs need to be unique per page; the linkedSignal names (`regFormCcs`, `displayName`, etc.) are already unique within a tab and work as ID candidates.
  - **Scope options**:
    - **A. Sweep all Configure tabs in one pass** — one PR, one pattern, one shared test. Largest PR but guarantees consistency.
    - **B. Sweep just Job Configuration (10 tabs)** — narrower but misses Administrators, Customers, Discount Codes, Age Ranges, Customer Groups.
    - **C. Sweep all uses of `field-label` in the app** — safest for accessibility-wide compliance. Largest change set but the `field-*` system is the right abstraction to update once.
    - **D. Introduce a shared `<tsic-form-field>` component** that renders a label+input with the `for`/`id` link automatic. Biggest refactor but every future form gets accessibility for free; future-proof.
    - **E. File-by-file as they're touched** — no sweep; accessibility improves opportunistically. Risk: gaps persist for untouched files.
  - **Recommendation**: C or D — both address the whole problem, but D is the more durable long-term answer. Decision for Todd based on how much refactor appetite there is.
  - **Todd, FYI**: Ann opted for a tracked PL rather than a silent patch on the Comms tab so the decision is explicit and consistent across the codebase.

### PL-050: Job Settings / Communications — consolidate field-help tip beneath all four email-list fields; reword
- **Area**: Menu Display / Job Settings → Communications tab
- **What I did**: Reviewed the "semi-colon delimited, no spaces" tip repeated beneath three of the four email-list fields
- **What I expected**: One tip after all four fields with clearer wording; in parentheses
- **What happened**: CC, BCC, and Reschedule each carried a separate tip ("semi-colon delimited, no spaces"); Always Copy Email List had no tip at all
- **Severity**: UX
- **Status**: Fixed
- **Note**: Applied on 2026-04-24 (revised after initial consolidation):
  - Removed the three per-field `field-help` tips with old wording from CC, BCC, and Reschedule rows.
  - Added the tip `(semi-colon between emails, no spaces)` directly after each of the four email-list labels (CC, BCC, Reschedule, Always Copy) — see [communications-tab.component.html:19-44](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L19-L44).
  - UI-only; no DB/DTO/service change.

### PL-049: Job Settings / Communications — rename CC/BCC Addresses to CC/BCC Email List for label parallelism
- **Area**: Menu Display / Job Settings → Communications tab
- **What I did**: Reviewed the four email-list fields on the Comms tab
- **What I expected**: Parallel "… Email List" naming across all four related fields so the section reads as a cohesive email-list configuration area
- **What happened**: Two used "Addresses" (CC Addresses, BCC Addresses) and two used "Email List" (Reschedule Email List, Always Copy Email List) — inconsistent terminology for the same kind of field
- **Severity**: UX
- **Status**: Fixed
- **Note**: Renamed on 2026-04-24:
  - "CC Addresses" → "CC Email List" ([communications-tab.component.html:19](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L19))
  - "BCC Addresses" → "BCC Email List" ([communications-tab.component.html:26](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L26))
  - No DB/DTO/service change — UI-only. Four Comms email-list fields now consistent.

### PL-048: Job Settings / Communications — rename "Disallow CC Player Confirmations" checkbox; consider tournament-only visibility
- **Area**: Menu Display / Job Settings → Communications tab
- **What I did**: Looked at the "Disallow CC Player Confirmations" checkbox on the Comms tab
- **What I expected**: Label that clearly states both the action (turn off) and the recipients (Player & Staff) and the scope (tournaments)
- **What happened**: Current label read "Disallow CC Player Confirmations" ([communications-tab.component.html:50](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L50)) — said nothing about BCCs, nothing about Staff, nothing about tournaments
- **Severity**: UX
- **Status**: Fixed (label); follow-up flagged below for visibility gate
- **Note**:
  - **Label rename** — updated to "TURN OFF Player & Staff Confirmations (CC & BCC) for tournaments" on 2026-04-24. Backing field stays `bDisallowCcplayerConfirmations` — no DB/DTO/service change.
  - **Visibility gate (still Open)** — new label says "for tournaments." If the setting only applies to tournament jobs, wrap the checkbox in `@if (jobTypeId === JobTypeTournament)` so it doesn't render on player/league/other sites. **Verify first**: does the backend actually gate the CC/BCC suppression on job type, or does it apply to any job with the flag set? If the latter, either tighten backend behavior to match the label, or drop "for tournaments" from the label. Open question for Todd.

### PL-047: Job Settings / Communications — swap order: Always Copy Email List should come before Reschedule Email List
- **Area**: Menu Display / Job Settings → Communications tab
- **What I did**: Reviewed the order of the email-list fields on the Comms tab
- **What I expected**: Always Copy Email List (general-purpose CC-on-every-email) before Reschedule Email List (narrower reschedule flow)
- **What happened**: Reschedule Email List appears first at [communications-tab.component.html:33-37](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L33-L37); Always Copy Email List at [line 40+](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L40)
- **Severity**: UX
- **Status**: Open
- **Note**: Swap the two `<div class="col-md-6">` blocks in the html. No model/DTO/service change — UI-only reordering.

### PL-046: Job Settings / Payment — Check/Mail-In/Balance section cleanup: TFPR move, two vestigial flags
- **Refs**: PL-042 (Teams Full Payment Required restructure)
- **Area**: Menu Display / Job Settings → Payment tab (Check / Mail-in / Balance section)
- **What I did**: Reviewed the three checkboxes at the bottom of Check/Mail-In/Balance: Teams Full Payment Required, Refunds in Prior Months, Allow Credit All
- **What I expected**: Each to be either actively used or removed; clear Director-facing explanation of what each does
- **What happened**: TFPR's placement is superseded by PL-042's restructure. The other two (`BAllowRefundsInPriorMonths`, `BAllowCreditAll`) appear to be **vestigial** — stored and edited, never consulted by runtime code
- **Severity**: Bug (zombie settings) + UX cleanup
- **Status**: Open
- **Note**: Findings from walkthrough on 2026-04-24:
  - **Teams Full Payment Required (TFPR)** — move per PL-042. Reference only; no separate action here.
  - **Refunds in Prior Months (`BAllowRefundsInPriorMonths`)** — no consumer found. Grep confirms the field lives only in: `Jobs` entity, `JobConfigDtos`, `JobConfigService` (CRUD), `JobCloneService` (carry-forward), `PaymentFeeRecalcTests` (pass-through property), and the Payment tab UI. Nothing in the new backend branches on it. **Legacy has real consumers** in `JobController.cs` / `Job_ViewModels.cs` — so the feature existed in Legacy but wasn't wired into the new system.
    - **Inferred Legacy semantic**: whether refunds/credits can be posted against prior/closed accounting periods (close March at month-end → can you later backdate a refund credit to March, or must it land in the current month?).
    - **Decision needed with Todd**: rewire the consumer (port Legacy refund-posting logic into the new accounting service and consult the flag) or remove the checkbox + DB column entirely.
  - **Allow Credit All (`BAllowCreditAll`)** — same story. No consumer in the new codebase; Legacy has references in `JobController.cs`, `Job_ViewModels.cs`, and `TSICHeader.cs`.
    - **Inferred Legacy semantic**: Director permission to zero-out an outstanding balance via a "credit all" action on an accounting row/registration.
    - **Decision needed with Todd**: same — rewire or remove.
  - **Zombie-setting risk**: Directors see the checkboxes and assume toggling them changes behavior. They don't. Leaving them in the UI as-is is actively misleading. Options:
    - **A. Remove from UI now** (keep DB columns for Legacy data, but strip from the Payment tab + DTOs + service). Safe; preserves migration history.
    - **B. Remove UI + DB columns** via migration. Cleanest but destructive.
    - **C. Keep UI, port Legacy behavior** into the new accounting service so the flags do something again. Most work, but matches Legacy parity if the features are still needed.
  - **Recommendation**: A for the immediate release (kill the misleading UI), then decide B vs C with Todd once the accounting rework scope is clearer.

### PL-045: Job Settings / Payment — clarify Balance Due % and Mail-in Payment Warning (gating, confirmations, Director help text)
- **Refs**: PL-042 (Teams Full Payment Required restructure — Balance Due % is only meaningful in deposit mode)
- **Area**: Menu Display / Job Settings → Payment tab (Check / Mail-in / Balance section)
- **What I did**: Reviewed the Check / Mail-in / Balance section and asked what Balance Due % and Mail-in Payment Warning actually do, when they apply, and whether they affect confirmations
- **What I expected**: Clear per-field semantics, consistent gating on payment method (CC Only vs CC or Check vs Check Only), verified downstream rendering in confirmations, and Director-facing help copy
- **What happened**: Current behavior is underspecified and at least one gap looks like a bug
- **Severity**: UX / Bug
- **Status**: Open — needs joint review with Todd
- **Note**: Findings from walkthrough on 2026-04-24:
  - **Balance Due %** (`Balancedueaspercent`): percent-as-string on `Jobs` ([Jobs.cs:30](TSIC-Core-Angular/src/backend/TSIC.Domain/Entities/Jobs.cs#L30)). Influences team fee calculation (`PaymentFeeRecalcTests`). Semantically, the portion collected as deposit at registration — remainder becomes the balance. **Only meaningful when `bTeamsFullPaymentRequired = false`**. Odd storage type (string vs decimal) worth questioning separately.
  - **Mail-in Payment Warning** (`MailinPaymentWarning`): free-text rendered as `alert-warning` banner on the team payment step ([payment-step.component.ts:174-177](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/steps/payment-step.component.ts#L174-L177)) and wired into player payment state ([payment-v2.service.ts:150](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/payment-v2.service.ts#L150)). **Rendered unconditionally today** — no guard for Allowed Methods. If the job is CC Only, the warning still shows when it shouldn't.
  - **Gap #1 (Bug)**: Mail-in Payment Warning should be suppressed unless Allowed Methods allows check (`paymentMethodsAllowedCode` ∈ {2 CC-or-Check, 3 Check-Only}). Fix: `@if (mailinPaymentWarning() && allowsCheck())` where `allowsCheck` checks the job's payment method.
  - **Gap #2 (Verify)**: do these values flow into **confirmation emails** and **on-screen confirmation** screens? No grep hit found in email templates — needs a check of `RegistrationConfirmation` / `TeamRegConfirmation` templates and on-screen Registration Complete pages.
  - **Gap #3 (UX)**: no Director-facing help text on either field. Proposed `.field-help` copy:
    - **Balance Due %** — *"Percentage of the total team fee collected as a deposit at registration. The remaining balance is invoiced/collected later. Only applies when 'Teams Full Payment Required' is unchecked."*
    - **Mail-in Payment Warning** — *"Message shown to parents/teams on the payment screen when they choose to pay by check. Only displayed if 'Allowed Methods' includes check."*
  - **Also worth a hint**: gray out / disable Balance Due % when `bTeamsFullPaymentRequired = true` (parallels its effective-no-op state).
  - **Next step**: joint review with Todd to confirm semantics, lock down the gating rule (payment method + deposit/full-pay), audit confirmation templates, and land the help copy.

### PL-044: Job Settings / Payment — move Refund Policy to Payment tab; show Player / Club Rep by job type
- **Area**: Menu Display / Job Settings → Payment (and Player, Coaches tabs)
- **What I did**: Reviewed where refund policies live today and proposed consolidating them under Payment
- **What I expected**: Refund policy editing under the Payment header, with job-type-aware visibility — Player Refund Policy for player sites; Club Rep / Team Refund Policy for tournament sites; both for league sites
- **What happened**: Today refund policies live on two separate tabs away from Payment — `PlayerRegRefundPolicy` on the Player tab, `AdultRegRefundPolicy` on the Coaches tab ([coaches-tab.component.html:41-48](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L41-L48))
- **Severity**: UX
- **Status**: Open — needs job-type mapping confirmation
- **Note**: Migration plan:
  - **Move** Player Refund Policy editor out of Player tab and Adult/Club Rep Refund Policy editor out of Coaches tab; both land in a new "Refund Policy" fieldset under the Payment tab.
  - **Visibility by `Jobs.JobTypeId`**:
    - **Player sites** — definition TBC: which JobTypeIds map here? Candidates: Camp (4), Club (1), some combination, or driven by a `CoreRegform` flag rather than JobTypeId. Confirm with Todd before implementation.
    - **Tournament (JobTypeId=2)** — show Club Rep / Team Refund Policy only.
    - **League (JobTypeId=3)** — show both.
  - **Field labels**: the underlying DB field is `AdultRegRefundPolicy` (shared by coach/club-rep flows since they all register via the adult path). Rename UI label to **"Club Rep / Team Refund Policy"** without changing the DB column.
  - **Downstream tab effects**:
    - **Player tab** — loses its Refund Policy field (audit the remaining content on that tab).
    - **Coaches tab** — loses its Refund Policy field; today that field is one of the main sections of the tab, so Coaches will read as thinner. Consider whether Coaches still earns its own tab after this move or consolidates elsewhere.
  - **Store Refund Policy** (`StoreRefundPolicy` on Mobile & Store tab) stays where it is — separate concern, not in scope.
  - **Decision points**:
    1. Confirm the JobTypeId mapping for "player sites."
    2. Confirm the "Club Rep / Team" label is preferred over existing "Adult" wording.
    3. Confirm Coaches tab survives after losing the refund field, or whether its other content should merge into an adjacent tab.
  - **Consolidation question (added 2026-04-25)**: should Player and Team refund policies share **one editor** (one source of truth) or stay separate? In practice most clubs have one refund policy that covers all registrations — maintaining two synced fields is tedious. Three sub-options:
    - **A. Keep two DB fields, one editor** — Payment-tab editor writes to **both** `playerRegRefundPolicy` and `adultRegRefundPolicy`. Downstream consumers unchanged. Cheapest, lowest-risk first step.
    - **B. Drop `adultRegRefundPolicy`, use `playerRegRefundPolicy` for both** — single source of truth; Team Registration reads `playerRegRefundPolicy`. Cleanest schema; small migration.
    - **C. One editor by default + "differentiate per role" toggle** — toggle reveals a second editor if a Director needs separate text. Most flexible; most code.
    - **Recommendation**: **A** at first ship (zero risk, immediate UX win), then **B** as a follow-up cleanup once we confirm no consumer relies on per-role text divergence.
    - **League-site impact** (the only job type with both flows visible): with A/B, one editor; with C, the toggle. With separate fields per the original PL-044 design, two editors stacked vertically.

### PL-043: Job Settings / Payment — hide Processing Fee fieldset entirely for non-SuperUsers
- **Refs**: PL-039 (Branding tab SuperUser-only), PL-041 (Per-Unit Charges SuperUser-only) — same pattern on the same tab
- **Area**: Menu Display / Job Settings → Payment tab
- **What I did**: Looked at the Processing Fee fieldset under Payment & Processing as a Director
- **What I expected**: The whole Processing Fee block to be absent for non-SuperUsers — platform-level pricing decisions shouldn't even be visible to Directors
- **What happened**: The fieldset renders for everyone with a lock icon on the legend and `[disabled]="!svc.isSuperUser()"` on every input ([payment-tab.component.html:32-66](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/payment-tab.component.html#L32-L66)) — Directors see "Processing Fee: Add Fee, %, Apply to Team Deposit" greyed out but readable
- **Severity**: UX / role visibility
- **Status**: Open
- **Note**: Today's lock+disabled approach gives Directors visibility into platform pricing levers without edit access — Ann prefers they don't see it at all. Two ways to land the change:
  - **A. Wrap the Processing Fee fieldset** in `@if (svc.isSuperUser()) { ... }`. Non-SuperUsers see only Allowed Methods on that row. Simplest.
  - **B. Move Processing Fee into a dedicated "SuperUser Only" subsection** at the bottom of the Payment tab alongside Per-Unit Charges (per PL-041). Clusters SuperUser-only Payment controls in one place.
  - **Layout consideration**: the row containing Processing Fee also contains Allowed Methods at `col-md-6`. If Processing Fee disappears, Allowed Methods would stretch or the column layout needs adjusting so the surviving field still reads cleanly.
  - **Backend hardening**: already gated correctly — `JobConfigService` applies the processing-fee field updates inside its `if (isSuperUser)` block, so API-level protection is in place. This PL is about frontend visibility only.
  - **Recommendation**: B — bundles with PL-041 into a single "SuperUser payment controls" subsection on the Payment tab. Cleaner than scattered `@if` blocks, consistent with how General handles SuperUser-only fields.

### PL-042: Job Settings / Payment — restructure "Teams Full Payment Required" as a Teams subsection with two radio options
- **Area**: Menu Display / Job Settings → Payment tab
- **What I did**: Reviewed the current "Teams Full Payment Required" checkbox under Payment and proposed a restructure
- **What I expected**: A **Teams** subheading under **Payment & Processing** presenting the full/deposit/balance options as a clear two-way choice instead of a single binary checkbox
- **What happened**: Today it's a single checkbox ([payment-tab.component.html:128-131](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/payment-tab.component.html#L128-L131)) — `bTeamsFullPaymentRequired` true → full pay at reg; false → deposit-only + later balance. A radio pair expresses the either/or choice more clearly than a checkbox whose "off" state isn't self-explanatory.
- **Severity**: UX
- **Status**: Open
- **Note**: Proposed structure:
  - New subheading **Teams** inside the existing **Payment & Processing** section.
  - **Two radio options** (mutually exclusive):
    1. **"Collect deposit ONLY now"** — maps to current `bTeamsFullPaymentRequired = false` (deposit at reg, balance later via reminders/invoice).
    2. **"Collect total due now (balance due amount will be added)"** — maps to current `bTeamsFullPaymentRequired = true` (no deposit/balance split — one full payment).
  - **No model change required** — the existing `bTeamsFullPaymentRequired` boolean covers both options; only the UI shape changes.
  - **Downstream behavior unchanged**: `JobConfigService` fee-recalc ([JobConfigService.cs:133-146](TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobConfigService.cs#L133-L146)) and `PaymentFeeRecalcTests` continue to work against the same boolean.
  - **Next step**: joint design review with Todd on UI copy and placement within Payment & Processing; low-risk since no DB/API change.

### PL-041: Job Settings / Payment — Per-Unit Charges section must be SuperUser-only
- **Refs**: PL-039 (parallel tab-level SuperUser-only gate for Branding); General tab's SuperUser Only section ([general-tab.component.html:58](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L58)) is the precedent pattern
- **Area**: Menu Display / Job Settings → Payment tab
- **What I did**: Logged in as Director and opened Job Settings → Payment
- **What I expected**: Per-Unit Charges (Per Player / Per Team / Per Month) to be SuperUser-only — these are platform-level pricing inputs Directors shouldn't see or edit
- **What happened**: Section renders unconditionally for all roles ([payment-tab.component.html:70-93](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/payment-tab.component.html#L70-L93)) — no role gate at all, even though sibling fields on the same tab already use `[disabled]="!svc.isSuperUser()"` on line 61
- **Severity**: Bug (role visibility + edit access)
- **Status**: Open
- **Note**: Two-layer fix required:
  - **Frontend options**:
    - **A. Hide the section from non-SuperUsers** — wrap the `<!-- Per-Unit Charges -->` block in `@if (svc.isSuperUser()) { ... }`. Matches the ask literally, one-line diff.
    - **B. Show fields disabled** to Directors — transparent but adds clutter.
    - **C. Move into a dedicated "SuperUser Only" subsection** under Payment, mirroring the General tab's SuperUser block. Cleanest if more payment fields go SuperUser-only later.
  - **Backend hardening** (required regardless of frontend choice): the Payment PUT endpoint must reject Director attempts to set `perPlayerCharge` / `perTeamCharge` / `perMonthCharge` — either via `if (isSuperUser) { ... }` guard in the service (the pattern `JobConfigService.General` already uses) or a per-field role check.
  - **Recommendation**: A now, then promote to C if more payment fields need the gate.
  - **Broader audit**: this gap on Per-Unit Charges suggests other "platform pricing" surfaces may also be missing role checks — sweep the Payment tab and adjacent tabs for any fields only SuperUsers should touch.

### PL-040: Branding — review overall with Todd re: fit with Widget menu, banner/bulletins display and sizing
- **Refs**: PL-039 (Branding tab SuperUser-only); Widget Editor lives at Configure → Widget Editor and overlaps conceptually
- **Area**: Menu Display / Branding / Widget Editor / Bulletins
- **What I did**: Reviewed the Branding tab and noted that banner rendering on the public-facing site isn't showing the entire image to users
- **What I expected**: Full banner image visible to users; clear separation of responsibilities between Branding (current Job Configuration tab) and Widget Editor (separate Configure menu item that manages home/dashboard widgets)
- **What happened**: Banner image is being clipped / cropped — users don't see the entire image. Broader question: how do Branding and Widget Editor fit together conceptually, and should their responsibilities be reorganized?
- **Severity**: Bug (clipping) + Question (architecture)
- **Status**: Open
- **Note**: Three threads to work through with Todd:
  1. **Banner display bug** — Branding tab captures Banner Background (max 1920px wide) and Banner Overlay (max 800px wide) separately ([branding-tab.component.ts:43, 57](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/branding-tab.component.ts#L43)). Rendering on public pages likely uses `background-size: cover` or `object-fit: cover` which crops to fit the container aspect ratio — that's why the whole image isn't showing. Options: switch to `contain` (shows full image with letterboxing), pick a fixed display aspect and document the crop expectation in the upload UI, or support multiple banner variants (desktop/mobile/tablet) so the right aspect ratio is delivered per viewport. Needs a look at the rendering component (likely header/hero on public job landing pages), not just the config tab.
  2. **Branding vs Widget Editor responsibilities** — both live under Configure, both affect what public users see. Today they're separate: Branding owns banner + color/brand assets; Widget Editor owns home/dashboard widgets. Question for Todd: is that split intuitive to Directors, or should some overlap be consolidated? (E.g., if banner is actually a "widget," does it belong in Widget Editor? Or should Widget Editor stay focused on data widgets and Branding stay focused on chrome?)
  3. **Bulletins display** — Ann suspects the bulletins rendering may also need rework to fit alongside banners more cleanly on the public site. Low-confidence observation from this pass; flag for side-by-side review when Branding display is tackled.
  - **Next step**: joint review with Todd before scoping any of the three threads into implementation.

### PL-039: Job Settings / Branding — remove from Director (and all non-SuperUser) menus; SuperUser only
- **Area**: Menu Editor / Job Settings tab visibility
- **What I did**: Looked at the Branding tab under Job Configuration
- **What I expected**: Branding to appear only for SuperUsers — Directors and other roles shouldn't see or access it
- **What happened**: Branding is unconditionally included in the tabs array ([job-config.component.ts:50](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.component.ts#L50)) — every user with Job Configuration access sees it
- **Severity**: Bug (role visibility)
- **Status**: Open
- **Note**: Two-layer fix required — hiding the tab without server-side gates leaves the API reachable via URL by anyone who knows the path:
  - **Frontend** — filter the `tabs` array so Branding is only emitted when `svc.isSuperUser()` is true. The service already exposes `isSuperUser` ([job-config.service.ts:57](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.service.ts#L57)), so it's a one-line conditional during `tabs` construction (or a computed signal that includes/excludes `branding` based on role).
  - **Backend** — audit and harden the three Branding endpoints currently consumed by the frontend:
    - `PUT /job-config/branding` (via `saveBranding`)
    - `POST /job-config/branding/images/{conventionName}` (via `uploadBrandingImage`)
    - `DELETE /job-config/branding/images/{conventionName}` (via `deleteBrandingImage`)
    Each should require the Superuser role via `[Authorize(Roles = "Superuser")]` (or whatever role-guard convention `JobConfigController` uses today). Directors hitting these URLs with a JWT should get 403, not 200.
  - **Also review**: whether similar tab-visibility gaps exist on other "SuperUser-intent" surfaces in Job Configuration — if Branding was missing the gate, others might be too.

### PL-038: Job Settings / General — Duplicate Job Path under SuperUser Only as an editable field
- **Area**: Menu Display / Job Settings → General
- **What I did**: Noted that Job Path is readonly on the General tab for everyone, including SuperUsers
- **What I expected**: SuperUsers able to edit Job Path from the SuperUser Only section for cases like typo fixes or rebrand re-slugging
- **What happened**: No edit path in the UI — SuperUser has to change Job Path via direct SQL today
- **Severity**: Feature
- **Status**: Open
- **Note**: Job Path is load-bearing — it's not just a label, so "make it editable" isn't a one-line UI change. Safety analysis:
  - **Primary URL segment**: `/:jobPath/...` is the top-level route prefix for every job-scoped screen ([app.routes.ts](TSIC-Core-Angular/src/frontend/tsic-app/src/app/app.routes.ts)).
  - **JWT claim**: per CLAUDE.md, jobPath is validated on every request — renaming invalidates every issued token.
  - **External links break**: confirmation/reschedule/waiver emails, QR codes, marketing materials, bookmarks — anything with the old URL printed or stored.
  - **In-flight registrations**: anyone mid-wizard gets booted.
  - **Uniqueness**: `JobCloneService` enforces jobPath+jobName uniqueness ([job-clone.component.ts:298](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L298)) — same check must fire on rename.
  - **Implementation options**:
    - **A. Plain editable field (SuperUser only)** — matches ask literally. Minimal UI, maximum risk. Minimum guardrails: confirmation dialog, uniqueness check, session-token invalidation.
    - **B. Editable only when the job has no registrations yet** (`jobCount === 0` or similar "draft" signal) — "fix a typo" mode. Disables itself once the job has any real traffic. Covers the common case without shipping a footgun.
    - **C. "Rename Job Path" wizard** — separate action that suspends the job, updates path, invalidates sessions, notifies Directors, optionally sets up an old→new redirect, reactivates. Safe for live jobs; heavier build.
    - **D. Keep readonly; renames continue via SQL** for rare cases. Zero cost, conservative.
  - **Recommendation**: B — matches Ann's ask, bounded by a draft-only gate so live jobs don't break. Escalate to C if live-job renames become frequent enough to justify the wizard.

### PL-037: Job Settings / General — Admin Expiry should land visually directly below User Expiry in the narrowed layout
- **Refs**: PL-022 (narrow workspace), PL-036 (Job Configuration tighter display), PL-031 (confirmed the two-field Expiry split is intentional)
- **Area**: Menu Display / Job Settings → General
- **What I did**: Noted that User Expiry (all users) lives in row 1 of Job Properties and Admin Expiry (SuperUser-only) lives in a separate section further down the page
- **What I expected**: Once the workspace is narrowed (PL-022), the two expiry fields read as a pair — Admin Expiry directly beneath User Expiry visually
- **What happened**: Today they're in two separate sections with unrelated grids, so at any width Admin Expiry doesn't align below User Expiry even though they're semantically the same concept split by role
- **Severity**: UX
- **Status**: Open
- **Note**: Possible implementations (decide during the PL-022 workspace pass):
  - **A. Group both into one "Expiry" micro-section** at the end of Job Properties — User Expiry always visible, Admin Expiry rendered inside the same section behind an `@if (svc.isSuperUser())` guard. Always paired regardless of viewport width. Cleanest; drops the one-field-only SuperUser section.
  - **B. Keep the SuperUser Only section** but position Admin Expiry in the same grid column as User Expiry above. Requires matching column layouts between the two sections so they align at every breakpoint — brittle if either row changes.
  - **C. Move Admin Expiry out of the generic SuperUser block** into a small labeled "Expiry (SuperUser override)" subsection that sits immediately below the Job Properties section. Less ambiguous about grouping, costs a little vertical chrome.
  - **Recommendation**: A — simpler markup, guaranteed alignment, and kills the "SuperUser Only" section's single-field appearance if Admin Expiry is the only SuperUser-specific expiry concern. Other SuperUser-only fields (Job Code, QBP Name, Sport, Job Type, Customer, Billing Type) stay in the SuperUser block as a group.

### PL-036: Job Configuration (all tabs) — overall display needs to be much tighter; defer to PL-022
- **Refs**: PL-022 (umbrella "all Configure pages adopt Nav Editor's narrow centered workspace")
- **Area**: Menu Display / Job Settings (all tabs: General, Player, Teams, Coaches, Payment, Scheduling, Branding, Communications, Mobile & Store, Dropdown Options)
- **What I did**: Reviewed the Job Configuration surface top-to-bottom during the walkthrough
- **What I expected**: Consistently tight, narrow-workspace presentation across every tab so Directors don't feel the page sprawl
- **What happened**: Overall density feels loose — same "full-viewport width + Bootstrap h2 default" issues called out elsewhere under Configure. Individual PL items have captured specific points (PL-033 Description height, PL-034 Sport dropdown, PL-035 QBP Name fallback), but the big-picture density is the PL-022 problem at Job Configuration scale.
- **Severity**: UX
- **Status**: Open
- **Note**: Defer to PL-022 — when the Nav Editor–style `.configure-page` workspace pattern lands, apply it to every Job Configuration tab (or to the shared `job-config.component` shell so all tabs inherit automatically). Pair with the PL-007 heading-size standard (Customer Groups' 1.5rem/700) and the PL-023 altrow contrast tune so Job Configuration reads as part of the same refreshed Configure family rather than a separate surface.

### PL-035: Job Settings / General — Confirm QBP Name is an override whose default is Event Name; wire up fallback if so
- **Area**: Menu Display / Job Settings → General (SuperUser section)
- **What I did**: Looked at the "QBP Name" field in the SuperUser section of General and asked whether it's an override with Event Name as the implicit default
- **What I expected**: Confirmation of the semantic, plus automatic fallback in consumers so that when QBP Name is blank, downstream code uses the Event Name
- **What happened**: The field exists and is editable/persisted, but **no fallback logic exists anywhere in the new codebase**. If `JobNameQbp` is null/empty, any consumer gets null/empty — not the Event Name.
- **Severity**: Question / possible Bug
- **Status**: Open
- **Note**: Findings from walkthrough on 2026-04-24:
  - **Persistence**: `Jobs.JobNameQbp` → SQL column `jobName_QBP` ([Jobs.cs:114](TSIC-Core-Angular/src/backend/TSIC.Domain/Entities/Jobs.cs#L114)). Editable on SuperUser section of General ([general-tab.component.html:77-82](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L77-L82)). Carried forward on job clone ([JobCloneService.cs:735](TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobCloneService.cs#L735)).
  - **Legacy evidence of intended default**: [HomeController.cs:178](reference/TSIC-Unify-2024/TSIC-Unify/Controllers/HomeController.cs#L178) populates the LastMonth_JobInvoice view model with `JobNameQbp = j.JobName` — Event Name substitutes for QBP Name in at least one reporting surface. Consistent with override-with-Event-Name-default semantic.
  - **Follow-ups if semantic confirmed**:
    1. **Audit downstream consumers** — currently only `JobConfigService` and `JobCloneService` reference `JobNameQbp` in the new code. Anything that exports to QuickBooks / generates invoices / sends billing communications needs to apply `JobNameQbp ?? JobName` (or a shared helper).
    2. **Central fallback**: add `Jobs.EffectiveQbpName => JobNameQbp ?? JobName` as a computed accessor (or a service-layer helper like `JobNamingService.GetQbpName(job)`) so every call site gets the fallback automatically.
    3. **UI copy**: "QBP Name" label gives no hint about override semantic. Add placeholder text like "Leave blank to use Event Name" or a `.field-help` paragraph beneath the field.
  - **Decision needed with Todd**: confirm the override-with-Event-Name-default semantic, then pick scope: minimal (UI copy only), full (UI copy + central fallback helper + downstream audit), or status-quo (document that QBP Name must always be set when needed).

### PL-034: Job Settings / General — Sport dropdown needs the same whitelist + title-case cleanup as LADT
- **Refs**: LADT PL-007 (Sport dropdown cleanup shipped there via `LadtService.GetSportsAsync`)
- **Area**: Menu Display / Job Settings → General (SuperUser section)
- **What I did**: Opened the Sport dropdown in the SuperUser section of the General tab
- **What I expected**: The clean 12-sport whitelist (title-cased, sorted) that LADT already uses per PL-007
- **What happened**: General's Sport dropdown pulls from a different code path and shows the full unfiltered `Sports` table — stale/irrelevant entries, no title-casing
- **Severity**: UX
- **Status**: Open
- **Note**: Two code paths surface Sports today:
  - **LADT** — `LadtService.GetSportsAsync` ([LadtService.cs:200-225](TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/LadtService.cs#L200-L225)) filters to whitelist (lacrosse, soccer, football, hockey, field hockey, basketball, baseball, softball, volleyball, wrestling, rugby, cheerleading) + title-cases + sorts.
  - **Job Config General** — `JobConfigService.BuildReferenceDataAsync` ([JobConfigService.cs:351](TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobConfigService.cs#L351)) calls `_repo.GetSportsAsync(ct)` with no filter or casing.
  - **Fix options**:
    - **A. Duplicate** the whitelist + filter/title-case into `JobConfigService.BuildReferenceDataAsync`. Self-contained; creates a second copy of the whitelist.
    - **B. Extract a shared helper** (`SportListHelper` / `ISportOptionProvider`) used by both services. One source of truth, no repo-layer presentation concerns.
    - **C. Push the filter into the repo** — every caller gets the clean list automatically. Most centralized but bakes presentation (title-case) into the repo.
  - **Recommendation**: B — right-sized DRY, keeps title-case out of the repo.
  - **Scope bonus**: audit any other Sport-pulling code paths (job clone wizard, customer-setup, reports) and route them through the same helper so no future surface drifts back to the raw table.

### PL-033: Job Settings / General — Description field height doesn't match sibling inputs on the same row
- **Area**: Menu Display / Job Settings → General
- **What I did**: Looked at the second row of Job Properties on the General tab — Job ID, Job Path, Description
- **What I expected**: All three fields the same visible height so the row reads as a single aligned band
- **What happened**: Description is a `<textarea rows="2">` while Job ID and Job Path are single-line `<input type="text">` — Description is visibly taller ([general-tab.component.html:40-53](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L40-L53))
- **Severity**: UX
- **Status**: Open
- **Note**: Two ways to align:
  - **A. Drop `rows="2"`** — make Description a single-line `<input>` (or `<textarea rows="1">`). Shortest, matches siblings. Loses multi-line editing for longer descriptions — probably fine since the field is short-form metadata.
  - **B. Keep multi-line but auto-grow** — `<textarea>` with a `min-height` matching the input line-height; let it expand as the user types. Preserves editing headroom without the default 2-row gap.
  - Recommendation: A if Description is truly short-form; B if Directors are expected to enter a paragraph. Check live data to see how long typical descriptions are.

### PL-032: Job Settings / General — Display Name: is it needed, and it's edited in two places
- **Area**: Menu Display / Job Settings → General (and Communications)
- **What I did**: Looked at the Display Name field on the General tab
- **What I expected**: Understand what Display Name is for and confirm it's not a duplicate of Customer Name
- **What happened**: Display Name on the jobs I viewed reads like the Customer Name — unclear whether the field is redundant
- **Severity**: Question
- **Status**: Open
- **Note**: Findings from walkthrough on 2026-04-24:
  - **Not the same as Customer Name.** `Jobs.DisplayName` is a separate persisted column on the Jobs table, editable per-job. Existing rows may coincidentally equal the customer name because Legacy migration or Directors typed the company name — but the field is independent.
  - **Main downstream use**: `FromName` on ARB recurring-billing reminder emails ([ArbDefensiveService.cs:196-199, 257-260](TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/ArbDefensiveService.cs#L196-L199)). Recipients see "DisplayName <adn@tsic.com>" as the email sender.
  - **Double-edit smell**: same `DisplayName` field is on BOTH General tab and Communications tab ([general-tab.component.ts:25](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.ts#L25) and [communications-tab.component.ts:17,29,57](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.ts#L17)). Two entry points for the same value — risk of confusion and save-order bugs.
  - **Decisions needed with Todd**:
    1. Keep or remove? If its only real job is the ARB FromName, consider collapsing into Communications tab only (where it's already visible) and dropping from General.
    2. If kept on both, label it consistently with a tooltip explaining what it's used for ("Sender name on ARB billing emails and page headers — defaults to your job name if blank").
    3. Audit whether anything else on the platform renders `Jobs.DisplayName` (page headers, public-facing pages) so the copy above is accurate.
  - **Recommendation going in**: remove from General, keep in Communications with a usage tooltip — single source of truth for what is essentially an email-branding field.

### PL-031: Job Settings / General — Legacy fields Administrators and JobAi not carried forward; Expiration confirmation
- **Area**: Menu Display / Job Settings → General
- **What I did**: Compared the new General tab against Legacy to see what's missing
- **What I expected**: Parity or a deliberate decision per field
- **What happened**: Three items to resolve — Expiration (already present, split), Administrators (separate page), JobAi (not surfaced)
- **Severity**: Question
- **Status**: Open
- **Note**: Findings from walkthrough on 2026-04-24:
  - **Expiration** — **already present, split into two fields**. User Expiry (visible to all, row 1 of Job Properties) controls public site lifecycle; Admin Expiry (SuperUser-only section) controls admin access lifecycle ([general-tab.component.html:32-37, 65-70](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L32-L37)). The split is intentional — different lifecycles. **Confirm with Todd that the split satisfies Legacy parity**; no action otherwise.
  - **Administrators** — not in General tab by design. Full feature lives at Configure → Administrators (a whole grid with add/remove/star/activate/role-picker). Options: (A) leave as-is; (B) add a small read-only "X Administrators" summary on General with a link to the full page. Recommendation: B — convenience without duplicating functionality.
  - **JobAi** — not surfaced today. `Jobs.JobAi` exists as the auto-increment integer identifier, used extensively on the backend (payments, reconciliation, search) but not exposed on the General tab. The General tab shows only Job ID (GUID) and Job Path (URL slug) as identifiers. Adding it is cheap — one more disabled/readonly field alongside Job ID in the Job Properties row, SuperUser-only (Directors don't need it). Requires surfacing `jobAi` on `JobConfigGeneralDto` from the backend.
  - **Recommendation bundle**: do JobAi (useful for SuperUser debugging) + Administrators summary link (nice convenience); leave Expiration as-is.

### PL-030: Dropdown Options — make value chips drag-reorderable so users can fix order without delete/re-add
- **Area**: Menu Display / Dropdown Options
- **What I did**: Looked at the value chips for categories under Configure → Dropdown Options (e.g. Jersey Sizes, Shorts Sizes) and noticed the values aren't in the order Ann wants
- **What I expected**: Ability to drag a chip to a new position and have the order persist
- **What happened**: No reorder mechanism — values render in insertion order, and the only mutations available are add (appends to end) and remove
- **Severity**: UX / Feature
- **Status**: Deferred (Superuser-only screen — release-push styling rule)
- **Note**: Backend + data model already support ordered arrays — no schema change needed.
  - **Current plumbing**: `JobDdlOptionsDto` carries each category as a `string[]`; `GET`/`PUT` preserve array order; new values are appended via `existing.push(val)` ([ddl-options.component.ts:152](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/ddl-options/ddl-options.component.ts#L152)). Dirty detection compares JSON, so an array-order change already triggers the Save bar.
  - **Precedent in-repo**: `@angular/cdk@21.0.5` already a dep; `cdkDrag` / `cdkDropList` already used in widget-editor, profile-editor, options-panel, and schedule build-order-tab. Proven pattern.
  - **Implementation sketch**:
    1. Add `DragDropModule` to component imports.
    2. `.chip-list` container → `cdkDropList` + `(cdkDropListDropped)="onChipReorder(cat.key, $event)"`.
    3. Each `.chip` → `cdkDrag`.
    4. `onChipReorder(key, event)`: clone array, call `moveItemInArray(arr, event.previousIndex, event.currentIndex)`, `this.options.set({...current, [key]: arr})`. Existing PUT already persists.
    5. Cursor cue (`cursor: grab; &:active { cursor: grabbing; }`) + `cdkDragPreview`/`cdkDragPlaceholder` to match profile-editor's drag ghost.
    6. Touch support is free with CDK.
  - **Optional quick-action to pair with drag**: per-category **"Alphabetize"** button — one click for common cases. Still need drag for custom orders (jersey sizes XS/S/M/L/XL/XXL isn't alphabetical).

### PL-028: Discount Codes — Expiry and Status columns both say "Active"; expired codes can still read "Active" in Status
- **Area**: Menu Display / Discount Codes
- **What I did**: Looked at the Expiry and Status columns on Configure → Discount Codes
- **What I expected**: Each column to say something semantically distinct, and an expired code to never read as "Active" anywhere on the row
- **What happened**: Collision — both columns use the word "Active" with different meanings, and an expired code can display **Expiry: "Expired"** and **Status: "Active"** simultaneously when the enable toggle is on
- **Severity**: UX / Bug
- **Status**: Open
- **Note**: Column logic today ([discount-codes.component.ts:147-156](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/discount-codes/discount-codes.component.ts#L147-L156)):
  - **Expiry** (via `getExpirationText`): "Expired" when `isExpired`, "Nd left" within 7 days, else **"Active"**.
  - **Status** (via `isActive`): **"Active"** when toggle on, else "Inactive".
  - Two problems: (1) word collision — happy-case row reads "Active … Active"; (2) stale Status — an expired code stays "Active" in Status until someone manually flips the toggle.
  - **Ann's proposal**: Expiry stays date-based but non-"Active" word in happy case; Status auto-derives to "Inactive" once expired.
  - **Options**:
    - **A. Both changes together** — (a) rename Expiry's happy-case "Active" to a date-based word ("In date" / "Valid" / or show the end date itself) and (b) make Status a computed value: `isExpired ? 'Inactive' : (isActive ? 'Active' : 'Inactive')`. Expiry answers "when"; Status answers "working now." Cleanest semantics.
    - **B. Status-only fix** — derive Status from `isExpired || !isActive`. Expired rows look right; word collision remains in the happy case.
    - **C. Expiry-only rename** — "Valid" / "Expired" / "Nd left" in Expiry; Status still purely toggle. Kills collision but an expired code can still read "Active" in Status.
  - **Recommendation**: A.
  - **Backend check before shipping**: confirm no downstream code treats `isActive === true` as "code is usable right now" without also checking `isExpired` — if any such code exists, either tighten those call sites or surface the same `!isExpired && isActive` derivation on the backend DTO.

### PL-027: Discount Codes date range — zero-pad month/day so dates align in the table
- **Area**: Menu Display / Discount Codes
- **What I did**: Looked at the "Dates" column on Configure → Discount Codes — e.g. on The Players Series: Girls Summer Showcase 2026
- **What I expected**: Month and day zero-padded (`04/07/2026 – 04/22/2026`) so every row's start/end lines up vertically at a glance
- **What happened**: Dates render unpadded (`4/7/2026 – 4/22/2026`) because [discount-codes.component.html:73-79](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/discount-codes/discount-codes.component.html#L73-L79) uses Angular's `'shortDate'` pipe. Columns don't align between rows with single- vs double-digit months/days
- **Severity**: UX
- **Status**: Open
- **Note**: Fix: swap `| date:'shortDate'` → `| date:'MM/dd/yyyy'` (or a shared format token) for both start and end. Worth sweeping other Configure tables rendering dates to keep the format consistent app-wide — candidates: Customers' Registered column (admin grid), Administrators' Registered column, any other table using `shortDate`.

### PL-026: Replace hidden trash can with lock icon + explanatory tooltip when delete is blocked
- **Area**: Menu Display / all Configure tables with conditional delete
- **What I did**: On Configure → Discount Codes (e.g. The Players Series: Girls Summer Showcase 2026 — code `Girls2026`), noticed no trash icon on a row because the code has been used
- **What I expected**: Some indication of *why* the row can't be deleted — a lock icon where the trash would go, with a hover tooltip "Cannot remove because the code has been used"
- **What happened**: Trash icon is simply omitted when `usageCount > 0` ([discount-codes.component.html:102-106](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/discount-codes/discount-codes.component.html#L102-L106)); user sees an empty action slot and has no explanation
- **Severity**: UX
- **Status**: Open
- **Note**: Same pattern exists on other Configure surfaces — this should be a consistent app-wide treatment, not a one-off:
  - Discount Codes — trash hidden when `usageCount > 0`
  - Customers — trash hidden when `jobCount > 0` ([customer-configure.component.html:69-73](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customers/customer-configure.component.html#L69-L73))
  - Administrators — similar guard pattern worth auditing
  - **Proposed markup**:
    ```html
    @if (canDelete) {
        <button class="icon-btn icon-btn-danger" (click)="confirmDelete(data)" title="Delete">
            <i class="bi bi-trash"></i>
        </button>
    } @else {
        <span class="icon-btn icon-btn-locked" aria-disabled="true"
              title="Cannot remove because the code has been used">
            <i class="bi bi-lock"></i>
        </span>
    }
    ```
  - Tooltip wording tailored per surface (code used / customer has jobs / admin has active assignments / etc.)
  - Scope decision needed:
    - **A. Discount Codes only** (as originally described) — smaller PR; follow-up sweep for sibling tables later
    - **B. Sweep all Configure delete-guards in one pass** — one shared `.icon-btn-locked` CSS token and consistent tooltips
    - **Recommendation**: B — consistency matters for a repeated interaction pattern; one PR lands the whole rule.

### PL-025: Nav Editor shows flag-gated items (e.g. Age Ranges) in "This Job" tree even when they'd be hidden at render time
- **Refs**: PL-008 (Age Ranges is flag-gated via `requiresFlags: ["teamEligibilityByAge"]` and hidden at runtime for jobs without `BYAGERANGE`)
- **Area**: Nav Editor
- **What I did**: Opened Nav Editor → This Job → "The Players series: Girls Summer Showcase 2026" → Director → Configure and saw **Age Ranges** listed there
- **What I expected**: Age Ranges hidden in the Nav Editor tree for any job that doesn't use age-range team eligibility — consistent with PL-008's runtime behavior
- **What happened**: Item is visible in the editor tree even though it would be filtered out of the Director's actual nav at render time
- **Severity**: UX / possible Bug
- **Status**: Open
- **Note**: Findings from walkthrough on 2026-04-24 — this is an editor-preview gap, not a live-menu bug.
  - **Architecture**: Nav Editor has two tabs, both showing **raw configuration** (not runtime-resolved nav):
    - Platform Defaults: `GetAllDefaultsAsync()` — every item in every role's default nav.
    - This Job: `GetJobOverridesAsync(jobId)` — job-specific override rows layered over the platform defaults.
  - **Runtime gating happens elsewhere**: `VisibilityRulesEvaluator` ([VisibilityRulesEvaluator.cs:62-64](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Services/VisibilityRulesEvaluator.cs#L62-L64)) only sets `teamEligibilityByAge` when the job's `CoreRegformPlayer` has `BYAGERANGE`. The Nav Editor doesn't run this resolution — it shows what's in config.
  - **Impact**: the live Director on Girls Summer Showcase 2026 still wouldn't see Age Ranges (assuming the job doesn't have BYAGERANGE — matches Ann's expectation). But the editor doesn't distinguish "this item is live for this job" from "this item exists in config but will be hidden at render because of `requiresFlags` / `jobTypes` / `sports` / `customersDeny`."
  - **Options**:
    1. **Visual cue** — render flag-gated-and-wouldn't-match items muted/italic/strikethrough with a "hidden by flag" badge. Preserves editability; adds clarity.
    2. **Filter out** when rules don't match. Cleanest view but loses in-context editing.
    3. **"Preview as rendered" toggle** on the This Job tab — flips between "show all config" vs "show only what this job sees."
    4. **Info tooltip/legend** explaining the tree shows config, not runtime resolution. Doc-only.
  - **Recommendation going in**: 1 or 3 — 1 is lower effort, 3 is more explicit for previewing. Both preserve the ability to edit hidden items.

### PL-024: Dropdown Options for Directors — should they see it under Configure, and how is it added?
- **Refs**: PL-019 (proposes removing the root-level Dropdown Options entry — but if Directors need a direct shortcut, that decision needs to change)
- **Area**: Menu Editor / Nav Editor
- **What I did**: Reviewed the new Nav Editor and asked whether Directors should have access to Dropdown Options under the root Configure section
- **What I expected**: Clear understanding of whether Directors need a direct root-level shortcut or the existing tab path is sufficient
- **What happened**: Today Dropdown Options is accessible to Directors **only** via Job Settings → Dropdown Options tab ([5) Re-Set Nav System.sql:122](scripts/5%29%20Re-Set%20Nav%20System.sql#L122) root entry is SuperUser-only at `0,0`; Job Settings at [line 117](scripts/5%29%20Re-Set%20Nav%20System.sql#L117) is Director-visible at `1,1` and carries the embedded tab). Need a product decision on whether to surface a direct root-level shortcut for Directors.
- **Severity**: Question
- **Status**: Open
- **Note**: Decision needed with Todd:
  - **Is a direct root-level shortcut needed for Directors?** — today Directors reach Dropdown Options through Job Settings → tab. Extra click, but contextually correct (dropdown options are job-scoped).
  - **How it would be added** (if yes):
    1. **SQL flip** — change the existing entry's visibility flags from `0,0` → `1,1` in `5) Re-Set Nav System.sql:122`, re-run the nav reset. Cheapest.
    2. **Nav Editor "Platform Defaults" tab** — same effect as #1 but through the new UI, no SQL touch.
    3. **Nav Editor "This Job" tab** — add a job-specific entry for Directors of one particular job. More surgical; useful if only certain clients need the shortcut.
  - **Interaction with PL-019**: PL-019 currently recommends removing the root entry entirely. If Directors need a root-level shortcut, PL-019's resolution changes from "delete" to "keep + flip Director-visible." The two items must be decided together.
  - **Recommendation going in**: resolve PL-019 first. If keeping access through Job Settings tab is acceptable for Directors (likely — it's contextually correct), remove the root entry per PL-019 and this item becomes moot. If Directors genuinely need the shortcut, flip visibility instead of removing.

### PL-023: Alternating row color contrast too subtle across all tsic-grid tables
- **Refs**: PL-003 (alternating rows shipped via SF grid migration, but contrast not strong enough)
- **Area**: Menu Display
- **What I did**: Viewed the Administrators table after the Syncfusion grid migration delivered alternating rows
- **What I expected**: A clear, scannable color difference between adjacent rows so the eye can follow a row across wide tables without losing its place
- **What happened**: The stripe exists (`--bs-body-bg` vs `--bs-tertiary-bg` per `_syncfusion-base.scss:99-105`) but the two tokens are close enough that at normal screen distance the table reads as a single block — rows still blend together
- **Severity**: UX
- **Status**: Open
- **Note**: Strengthen the altrow contrast globally (all `tsic-grid-*` variants) so the fix applies to every SF grid in the app, not just Administrators. Options: (a) swap `--bs-tertiary-bg` for `--bs-secondary-bg` or a palette-aware mid-tone; (b) introduce a dedicated `--grid-altrow-bg` token so contrast can be tuned without affecting other surfaces using `--bs-tertiary-bg`. Test across all 8 palettes + light/dark to keep WCAG contrast and palette-switch fidelity.

### PL-022: All Configure pages should use a narrow centered workspace like Nav Editor — Administrators and Customers feel too wide
- **Area**: Menu Display
- **What I did**: Compared the Administrators and Customers pages against the Nav Editor page
- **What I expected**: All Configure pages to use the same tighter workspace styling Nav Editor uses — a centered card/heading with a capped width
- **What happened**: Administrators and Customers stretch to the full viewport width, so the heading sits far left, the grid sprawls edge-to-edge, and the page feels loose. Nav Editor on the same viewport is visibly tighter and easier to scan. Evidence: [nav-editor.component.scss:7-11](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/nav-editor/nav-editor.component.scss#L7-L11) uses `max-width: 1200px; margin: 0 auto; padding: 2rem;`. Administrators / Customers don't constrain width at all.
- **Severity**: UX
- **Status**: Won't Fix
- **Note**: Apply the Nav Editor layout pattern as the standard for every Configure page (Administrators, Customers, Customer Groups, Discount Codes, Age Ranges, Dropdown Options, Theme, Widget Editor, Job Clone, Report Catalogue, Job Settings). Ideal fix: promote the `max-width: 1200px; margin: 0 auto; padding: 2rem;` rule to a shared `.configure-page` utility (or reuse an existing one if already in `_utilities.scss`) so every Configure page inherits the same bounds — and future Configure pages pick it up automatically. Verify on wide monitors that the grid columns still breathe at 1200px; if not, revisit the cap.

### PL-021: Configure submenu items no longer in alphabetical order — confirm intended ordering logic
- **Area**: Menu Editor
- **What I did**: Opened the Configure dropdown and scanned the submenu items
- **What I expected**: Alphabetical ordering as was the case previously, so items are predictable to find
- **What happened**: Items appear in this order: Job Settings, Discount Codes, Age Ranges, Administrators, Customer Groups, Dropdown Options, Customers, Theme, Nav Editor, Widget Editor, Job Clone, Report Catalogue. Looking at `scripts/5) Re-Set Nav System.sql:117-128`, the list is grouped by role visibility — the first three are Director-visible and the rest are SuperUser-only — so there *is* a grouping rationale, but within each group the ordering is not alphabetical.
- **Severity**: Question
- **Status**: Won't Fix
- **Note**: Two things to confirm with Todd:
  1. Is the **role-grouping** (Director-visible first, SuperUser-only second) intentional and worth keeping? If yes, call it out in a tooltip/section header so users don't read it as random.
  2. Within each group, should items be **alphabetized**? Current SuperUser-group order (Administrators, Customer Groups, Dropdown Options, Customers, Theme, Nav Editor, Widget Editor, Job Clone, Report Catalogue) is neither alphabetical nor obvious frequency-of-use. Alphabetizing within groups would restore the "predictable to find" property Ann expected without losing the grouping rationale.
  - **Options (from walkthrough 2026-04-24)**:
    - **A. Alphabetize within groups** — Directors see Age Ranges / Discount Codes / Job Settings; SuperUser sees Administrators / Customer Groups / Customers / Dropdown Options / Job Clone / Nav Editor / Report Catalogue / Theme / Widget Editor. Preserves role-grouping rationale, restores predictability.
    - **B. Fully alphabetize across all items** — ignore role-grouping. Simplest. Downside: Job Settings (most used) lands mid-list.
    - **C. Keep current order + add tooltip/section header** explaining the grouping so it doesn't read as random.
    - **D. A + C combined** — alphabetize within groups and surface the grouping.
  - **Recommendation going in**: A — lowest-risk win. Implementation: one-file edit in [5) Re-Set Nav System.sql:117-128](scripts/5%29%20Re-Set%20Nav%20System.sql#L117-L128) reassigning `SortOrder`.

### PL-020: Profile Editor placement — should Directors see it under Job Settings / Player Registration?
- **Area**: Menu Editor
- **What I did**: Looked at where Profile Editor currently lives
- **What I expected**: Easy access for Directors in the context where they'd use it
- **What happened**: Profile Editor is currently under Tools. Some of that info would be helpful to Directors under Job Settings > Player Registration subheader. Let's discuss placement and what Directors should see.
- **Severity**: Question
- **Status**: Won't Fix
- **Note**: Findings from walkthrough on 2026-04-24 — decision needed with Todd:
  - **Placement today**: under Tools section ([5) Re-Set Nav System.sql:157](scripts/5%29%20Re-Set%20Nav%20System.sql#L157)).
  - **Role visibility today**: SuperUser-only at both nav level (`0,0` in director-visibility columns) and route level (`data: { roles: [Roles.Superuser] }` at [app.routes.ts:296-299](TSIC-Core-Angular/src/frontend/tsic-app/src/app/app.routes.ts#L296-L299)). Directors don't see it at all.
  - **What it is**: page title is "Profile Metadata Editor" — a **platform-wide tool** for editing profile metadata templates that any job's player-registration form draws from, plus "New Profile" and a link to the migration dashboard.
  - **Key distinction**: the current component is a platform-level editing tool (SuperUser-only by design — letting Directors write/delete templates would let one customer change templates that other jobs use). What Directors likely want is a **scoped, read-only summary** of which profile this job uses and what fields it collects.
  - **Options**:
    - **A. Add read-only "Profile in use" panel** to Job Configuration → Player tab. Directors see assigned profile, field count, maybe field list. No editing. Platform editor stays in Tools for SuperUsers. Safest.
    - **B. Move Profile Editor to Configure + flip Director-visible.** Simpler but lets Directors edit platform-wide profiles — risk of cross-customer contamination.
    - **C. Split the component** — extract a Director-scoped view + request-change flow; keep full editor SuperUser-only.
    - **D. Leave as-is** — Directors request changes from TSIC when needed.
  - **Recommendation going in**: A — Directors get the visibility they lack today, platform editing stays properly gated.

### PL-019: "Configure Dropdown Options" — already a submenu under Job Configuration, remove from root level
- **Area**: Menu Editor
- **What I did**: Noticed "Configure Dropdown Options" appears at the root Configure level
- **What I expected**: It to only appear as a submenu under Job Configuration since it's already there
- **What happened**: It's in both places — don't think it's needed at the root level
- **Severity**: UX
- **Status**: Won't Fix
- **Note**: Both locations confirmed on 2026-04-24:
  1. Root Configure menu entry at [5) Re-Set Nav System.sql:122](scripts/5%29%20Re-Set%20Nav%20System.sql#L122) routing to `configure/ddl-options` (SuperUser-only).
  2. Embedded as the `'ddlOptions'` tab in [job-config.component.html:63](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.component.html#L63) with label from [job-config.service.ts:223](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.service.ts#L223). Same `DdlOptionsComponent` surfaced in both places.
  - **Fix**:
    1. Delete the root `Dropdown Options` INSERT at `5) Re-Set Nav System.sql:122`.
    2. Mirror the delete in `scripts/0-Restore-DevConfig-PROD.sql:722-723` so dev-env restore doesn't reintroduce it.
    3. Re-run the nav reset script so the change lands in DB.
    4. Leave the `configure/ddl-options` route registered in `app.routes.ts` — the Job Configuration tab mounts the component directly (doesn't route), and the route still works for anyone with a deep link bookmark.

### PL-018: Timezone dropdown missing Arizona Time
- **Area**: Menu Editor
- **What I did**: Looked through the timezone dropdown options when editing a customer
- **What I expected**: Arizona Time to be available (Arizona doesn't observe DST, so it's a distinct timezone)
- **What happened**: No Arizona Time option in the dropdown
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: Timezones is a small DB lookup table ([Timezones.cs](TSIC-Core-Angular/src/backend/TSIC.Domain/Entities/Timezones.cs)) — `TzId`/`TzName`/`UtcOffset`/`UtcOffsetHours`/audit fields. No seed script in repo; rows carried from Legacy. Fix (if Arizona is indeed missing — couldn't enumerate rows from codebase): one-row INSERT into `reference.Timezones`, e.g. `('US Arizona Time', -420, -7, NULL, GETDATE())` — Arizona uses MST year-round at UTC−7. Mirror whatever offset convention existing rows use before running.
  - **Linked to PL-015**. If PL-015 resolves as "remove the customer-level timezone field entirely," PL-018 becomes moot — resolve PL-015 first to avoid adding a row to a doomed table.
  - **DB change, not a code edit** — goes through Todd's preferred migration path (script in `scripts/`, run manually in prod).

### PL-017: Edit Customer — Timezone popup shows "Afghanistan Time" but table shows "Eastern Time"
- **Area**: Menu Editor
- **What I did**: Edited a customer and looked at the Timezone field in the popup
- **What I expected**: Timezone in the popup to match what the table shows (Eastern Time)
- **What happened**: Popup shows "Afghanistan Time" but the table reads "Eastern Time" — something is wrong with the timezone field defaulting or display
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: Root cause isolated on 2026-04-24 — async-option-render race, not a data bug. The customer-dialog uses native `[value]=` binding on the `<select>` ([customer-dialog.component.html:38](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customers/customer-dialog/customer-dialog.component.html#L38)) while `timezones` arrives via parent `@Input()` on a separate HTTP call. Timezones return alphabetically sorted by `TzName` ([CustomerRepository.cs:91-101](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/CustomerRepository.cs#L91-L101)) → "Afghanistan Time" is option[0]. Sequence: dialog renders with empty options → detail HTTP sets `tzId` → timezones HTTP lands and mutates `<option>` set → browser drops selection and shows option[0]. Grid is correct (reads stored Eastern); dialog silently misleads. If user saves without touching the field, `(change)` never fires and Eastern stays in DB — but dialog has already "lied" about the current value.
  - **Fix**: swap native `[value]=` for `[ngModel]`:
    ```html
    <select [ngModel]="tzId()" (ngModelChange)="tzId.set($event)" ...>
    ```
    Angular FormsModule re-applies model value when options mutate. FormsModule already used elsewhere in the dialog stack (admin-form-modal uses it for role select) — no new dependency.
  - **Independence from PL-015**: fix this regardless of the broader "is timezone needed at all" decision — dialog shouldn't silently misrepresent stored state even if field is eventually removed.

### PL-016: Customers — split into "Active Customers" and "Inactive Customers" tables
- **Area**: Menu Display
- **What I did**: Looked at the Customers table
- **What I expected**: Easy way to distinguish active from inactive customers
- **What happened**: Customers with 0 Jobs are effectively inactive but mixed in with active ones — creates noise. Split into two tables: "Active Customers" at the top and "Inactive Customers" below to clean it up.
- **Severity**: UX
- **Status**: Won't Fix
- **Note**: No `BActive`/`IsActive` field on the Customer entity — "inactive" is purely derived from `JobCount === 0` ([CustomerRepository.cs:69](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/CustomerRepository.cs#L69)). UI-only change, no backend/schema needed. Options:
  - **A. Two separate grids on the page** — Active (jobCount > 0) on top, Inactive (jobCount = 0) below. Two `<ejs-grid>` blocks fed from filtered arrays. Matches the ask literally; cleanest visual split.
  - **B. Single grid with Syncfusion grouping** by active/inactive — collapsible groups.
  - **C. Single grid with an Active/Inactive/All filter chip** at the top.
  - **D. Single grid, sort inactive to the bottom** — minimal change, weakest split.
  - **Recommendation**: A — matches the literal ask with the least added UX complexity.

### PL-015: Customers table — all show Eastern Time even for non-Eastern customers. Is time zone needed?
- **Area**: Menu Display
- **What I did**: Looked at the Customers table entries
- **What I expected**: Correct time zones per customer, or no time zone if not needed
- **What happened**: All customers show "Eastern Time" or "US Eastern Time" — even YJ Midwest. Is the time zone field needed at all?
- **Severity**: Question
- **Status**: Won't Fix
- **Note**: Findings from walkthrough on 2026-04-24 — decision needed with Todd:
  - **Not a display bug.** The grid faithfully shows whatever `Customers.TzId` is in the database ([CustomerRepository.cs:68](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/CustomerRepository.cs#L68)). YJ Midwest reading "Eastern Time" means the DB row literally points at the Eastern `Tz` row — data quality issue from imports/defaults, not rendering.
  - **Field appears unused in the new system.** Every hit on `Customers.TzId` lives inside the Configure Customers CRUD path itself (repository, service, DTO, edit dialog). No scheduling logic, email send windows, display formatting, or job defaulting reads it. The only other reference is in legacy `reference/TSIC-Unify-2024/Controllers/Admin/CustomerController.cs` — not shipping. Effectively vestigial: collected, stored, displayed, validated, and never consulted.
  - **Options**:
    - **A. Remove entirely** — drop column from grid, DTO, edit dialog, plus DB migration to drop `Customers.TzId`. Cleanest if Todd confirms unused.
    - **B. Keep data, hide column** — remove Timezone from the grid only; leave field in the edit dialog. Low-risk, preserves data, declutters grid.
    - **C. Keep as-is and clean the data** — only makes sense if something *will* consume it.
    - **D. Defer to post-release decision** — leave untouched until new system is live.
  - **Recommendation going in**: B as the safe middle ground; A if Todd confirms no future use.

### PL-014: Configure Customers — reduce heading size and rename from "Customer Configure" to "Customers"
- **Area**: Menu Display
- **What I did**: Opened Configure Customers page
- **What I expected**: Heading sized consistently with other pages, labeled simply "Customers"
- **What happened**: Heading is too large (same issue as PL-007) and says "Customer Configure" — should just say "Customers"
- **Severity**: UX
- **Status**: Deferred (Superuser-only screen — release-push styling rule; rename portion already shipped)
- **Note**: **Rename** done on 2026-04-24 — h2 at [customer-configure.component.html:3](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customers/customer-configure.component.html#L3) changed from "Customer Configure" → "Customers" (nav label already says "Customers", so the page now matches). **Heading size** half defers to PL-022 / PL-007 — same family of Configure-pages-heading-size standardization; will be resolved together.

### PL-013: Customer Groups — Add and Delete buttons too far from customer names in right table
- **Area**: Menu Display
- **What I did**: Looked at the Add and Delete functions next to the customer names in the right table
- **What I expected**: Buttons positioned close to the customer names
- **What happened**: Too much space between the buttons and the customer names — move them much closer
- **Severity**: UX
- **Status**: Deferred (Superuser-only screen — release-push styling rule)
- **Note**: Cause isolated on 2026-04-24 — `.member-item` uses `justify-content: space-between` ([customer-groups.component.scss:168-172](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.scss#L168-L172)), which pins the × delete button to the far right of the 2fr-wide member panel. "Add" isn't per-row — it's a dropdown row at the top (`.add-member-row`), so this is effectively about the per-row Delete (×) placement. Fix: swap `justify-content: space-between` for `flex-start` and add `gap: var(--space-2)` so the × sits next to the customer name. Pair with PL-022 workspace pass if member-panel width is also narrowed.

### PL-012: Customer Groups — "Members of [group name]" header needs visual emphasis
- **Area**: Menu Display
- **What I did**: Clicked into a Customer Group and saw the header "Members of 'STEPS Lacrosse LLC'"
- **What I expected**: The group name to stand out visually — highlighted, bold, or styled differently
- **What happened**: Header doesn't stand out enough — needs some kind of highlighting so the group name is clearly visible
- **Severity**: UX
- **Status**: Deferred (Superuser-only screen — release-push styling rule)
- **Note**: Current markup at [customer-groups.component.html:131](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.html#L131) — `<span class="panel-title">Members of "{{ selectedGroup()!.customerGroupName }}"</span>` — group name is inline literal text inside a flat span, no emphasis. Fix: split the name into its own element and style distinctly. Options: **(A)** bold via `<strong>`; **(B)** accent color (`var(--bs-primary)` / `var(--brand-text)`); **(C)** badge-style pill after the "Members of" prefix (closest to "highlighted"); **(D)** bold + accent (B+A combination). Pick during PL-022 workspace pass so typography choice stays coherent with the shared Configure page style.

### PL-011: Customer Groups — remove total group count, add column heading like "Member Jobs" or "Jobs Included"
- **Area**: Menu Display
- **What I did**: Looked at the Customer Groups table
- **What I expected**: A meaningful column heading above the job count numbers
- **What happened**: Shows total number of groups (currently 5) which isn't useful. Remove it and instead add a heading above the numbers in each row, like "Member Jobs" or "Jobs Included", so it's clear what the numbers represent.
- **Severity**: UX
- **Status**: Deferred (Superuser-only screen — release-push styling rule)
- **Note**: Findings from walkthrough on 2026-04-24:
  - **Total-groups badge** to remove: [customer-groups.component.html:39](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.html#L39) — `<span class="badge bg-secondary">{{ groups().length }}</span>`.
  - **Per-row count** is on [line 77](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.html#L77) — `group.memberCount`.
  - **Label correction needed**: the count is **customers**, not jobs. The data model treats group members as customers (add-member dropdown at line 140 says "Select a customer to add"; `memberCount` is incremented when a customer is added at line 202). Labeling the column "Member Jobs" or "Jobs Included" would be misleading — a group showing "5" has 5 *customers* who collectively own many more jobs.
  - **Recommended label options**: "Customers" (most literal), "Members" (matches code's internal term), or "Customer Count" (most explicit). Decision needed.
  - **Layout note**: Customer Groups is a master-detail view, not a true table, so there's no column-header row. Implementation: add a small label row above the group list reading `Group · Customers` (or chosen label) with the count right-aligned under the label.
  - **Three changes in one edit**: (1) remove total-groups badge, (2) add the column label row, (3) align subtitle wording ("Organize customers…") with the chosen column label so the two agree.

### PL-010: All tables under Configure use too much space — compress to match Legacy sizing
- **Area**: Menu Display
- **What I did**: Looked at tables across all Configure menu pages
- **What I expected**: Compact tables like Legacy
- **What happened**: All tables under Configure have too much whitespace — rows, columns, and overall spacing should be much smaller. Legacy tables are a good reference for sizing.
- **Severity**: UX
- **Status**: Won't Fix
- **Note**: Defer to PL-022. The `3ba0994a` refactor migrated 4 of 5 Configure list pages (Administrators, Customers, Discount Codes, Age Ranges) to Syncfusion `ejs-grid` with `cssClass="tsic-grid-tight"` — the app's maximum-compression density variant. Customer Groups was **not** migrated and still uses a hand-rolled layout; pick it up when PL-022's workspace standardization lands so every Configure page finishes on the same grid + density pattern.

### PL-009: Customer Groups — change subtitle to "Organize customers into named groups for Customer/Job Revenue reporting"
- **Area**: Menu Display
- **What I did**: Read the Customer Groups subtitle under Configure
- **What I expected**: Description that specifies what the reporting is for
- **What happened**: Says "Organize customers into named groups for reporting" — should say "Organize customers into named groups for Customer/Job Revenue reporting"
- **Severity**: UX
- **Status**: Fixed
- **Note**: Subtitle updated to "Organize customers into named groups for Customer/Job Revenue reporting" in [customer-groups.component.html:4](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.html#L4).

### PL-008: Age Ranges menu — hide it but keep the function available for future clients?
- **Area**: Menu Editor
- **What I did**: Noticed the Age Ranges menu item under Configure
- **What I expected**: Only menus relevant to current customers
- **What happened**: Age Ranges isn't used by any current customers. Should we hide this menu but keep the function available in case a new client in a new sports category needs it?
- **Severity**: Question
- **Status**: Fixed
- **Note**: Already implemented as asked. The Age Ranges nav entry carries `requiresFlags: ["teamEligibilityByAge"]` ([5) Re-Set Nav System.sql:119](scripts/5%29%20Re-Set%20Nav%20System.sql#L119)), and the `VisibilityRulesEvaluator` only sets that flag for jobs whose `CoreRegformPlayer` has `BYAGERANGE` in the second pipe position ([VisibilityRulesEvaluator.cs:62-64](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Services/VisibilityRulesEvaluator.cs#L62-L64)). Menu auto-hides for every current customer; auto-appears for any future job that enables age-range team eligibility. Shipped via commit `b429224a feat(nav): add requiresFlags dimension + section-level rule overlay`.

### PL-007: Under Configure, all menu page headings should match "Customer Groups" heading size — many are too large
- **Area**: Menu Display
- **What I did**: Browsed through menu items under Configure
- **What I expected**: All page headings the same font size
- **What happened**: Many headings are too large — should all match the size used for "Customer Groups"
- **Severity**: UX
- **Status**: Fixed
- **Note**: Resolved via global-only typography (no inline, no per-component overrides). Per Todd's call, font-size kept at the existing global `--font-size-lg` (not bumped to Customer Groups' 1.5rem reference). Three component overrides removed so global wins: `customer-groups.component.scss` `.page-title` block deleted; `job-config.component.scss` `.page-title` trimmed to layout-only (`text-align: center`); `push-notification.component.scss` `.page-title` trimmed to layout-only (`text-align: center; margin-bottom`). Director-visible Configure pages still using `<h2 class="mb-0">` switched to `<h2 class="page-title">`: `discount-codes.component.html`, `configure-age-ranges.component.html`. Administrators left untouched (Superuser-only — release-push styling rule).
- **Note**: Defer to PL-022. Concrete target confirmed during walkthrough on 2026-04-24: Customer Groups uses `<h2 class="page-title">` with a local override (`font-size: 1.5rem; font-weight: 700;` per [customer-groups.component.scss:13-16](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.scss#L13-L16)). Other Configure pages render at three different sizes: Administrators/Widget Editor/Age Ranges/Discount Codes/Customer Configure at Bootstrap h2 default (~2rem) via `mb-0`; Job Config/DDL Options at `var(--font-size-lg)` (~1.125rem) via the global `.page-title`; Nav Editor at `<h1>`. Fix: promote Customer Groups' local override to the global `.page-title` in `_utilities.scss` (or introduce a `--page-title-font-size` token) and apply `class="page-title"` consistently to every Configure page — folds into PL-022's workspace standardization.

### PL-006: Add Administrator — how does someone become eligible in the Username search? Dropdown options seem random.
- **Area**: Menu Editor
- **What I did**: Clicked "Add Administrator" and looked at the Username search dropdown
- **What I expected**: Clear list of eligible users, or understanding of how someone becomes eligible to be added
- **What happened**: Dropdown seems to have random options — not clear how a person registers or qualifies to appear in the Username search
- **Severity**: Question
- **Status**: Deferred
- **Note**: Findings from walkthrough on 2026-04-24 — decision needed with Todd.
  - **Current behavior**: the search queries the **entire platform** — every `AspNetUsers` row, no role filter, no customer scope. Per [UserRepository.cs:166-190](TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/UserRepository.cs#L166-L190): case-insensitive `contains` across `UserName`, `FirstName`, `LastName`, ordered by last name, top 10. Effective eligibility rule today: *"has an account anywhere in the TSIC platform"* — so parents, players, unrelated-customer staff, and platform-wide users all appear mixed together. The "random" feel is because the 10-result cap surfaces an arbitrary alphabetical slice once the query is loose.
  - **Concerns to resolve**:
    1. **Cross-customer leakage** — a Director at Customer A can add a user from Customer B as an admin of their own job. Almost certainly not intended.
    2. **No role awareness** — parents/players are indistinguishable from staff in the dropdown (only name + username shown, no role/customer hints).
    3. **No customer scope filter** — common expectation ("suggest people already associated with my customer") isn't enforced.
  - **Options**:
    - **A. Scope to same customer** — only users with an existing admin Registration under a job owned by the current customer.
    - **B. Platform-wide but role-filtered** — exclude users whose only registrations are parent/player; require at least one Director/SuperDirector/Superuser role somewhere.
    - **C. Leave wide open + add role/customer label to each dropdown row** so the director can tell who's who.
    - **D. Default to A, with an explicit "search all users" toggle** for the rare cross-customer add.
  - **Recommendation going in**: A or D — both eliminate the random feel and block accidental cross-customer additions.

### PL-005: Star icons — move before names, clarify default contact, and confirm job clone behavior
- **Area**: Menu Display
- **What I did**: Looked at the star icons used to set primary contact in the Administrators table
- **What I expected**: Stars positioned before the name for easier scanning; clear understanding of default behavior
- **What happened**: Three items: (1) Consider moving star icons to the left of the name, (2) Who is the default contact if none is selected? (3) Will the selected Director carry forward when cloning a job?
- **Severity**: Question
- **Status**: Deferred
- **Note**: Findings from walkthrough on 2026-04-24:
  1. **Star position**: still in far-right Actions column, not next to the name — untouched.
  2. **Default contact**: no default exists. `Jobs.PrimaryContactRegistrationId` is nullable; the toggle either sets a specific `registrationId` or sets it back to `null` ([AdministratorService.cs:185-188](TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/AdministratorService.cs#L185-L188)). Any downstream consumer (notifications, escalations) must handle "no primary contact" on its own. Decision needed: leave nullable, or add a deterministic fallback (e.g., oldest Director).
  3. **Job clone carry-forward**: primary contact does **not** carry forward. `JobCloneService` copies admin Registrations but never copies `Jobs.PrimaryContactRegistrationId` — grep returns no match. Cloned job lands with no primary contact set.
  4. **Role gating**: star icon only renders for `roleName === 'Director'` rows — SuperDirectors and Superusers can't be set as primary contact. Confirm with Todd this is intentional.
  5. **Legacy migration (REQUIRED)**: primary contact on Legacy must be carried forward into the new system during migration — star shown next to the Director who was the primary contact on Legacy so customer continuity is preserved. Verify the Legacy migration script/backfill sets `Jobs.PrimaryContactRegistrationId` from the equivalent Legacy field, and eyeball a handful of migrated jobs to confirm the star lands on the expected Director.

### PL-004: Administrators table — too much spacing, compress rows and columns
- **Area**: Menu Display
- **What I did**: Looked at the overall Administrators table layout
- **What I expected**: Compact table with items close together
- **What happened**: Table has too much whitespace — rows and columns can be much smaller and tighter overall
- **Severity**: UX
- **Status**: Deferred
- **Note**: Defer to PL-022 — overall table tightness folds into the broader "all Configure pages use the narrow Nav Editor workspace" fix. Resolve together.

### PL-003: Administrators table — use alternating row colors like Search Player table
- **Area**: Menu Display
- **What I did**: Looked at the Administrators table rows
- **What I expected**: Alternating row colors to make rows easy to distinguish, like the Search Player table
- **What happened**: No alternating colors — rows blend together
- **Severity**: UX
- **Status**: Fixed
- **Note**: Alternating rows shipped with the Syncfusion grid migration (commit `3ba0994a`) — odd rows on `--bs-body-bg`, even rows on `--bs-tertiary-bg`, same as Search Registrations. See `_syncfusion-base.scss:99-105`. Follow-up on contrast strength tracked separately in PL-023.

### PL-002: "Administrators" heading too large — match "Search Registrations" header font/size
- **Area**: Menu Display
- **What I did**: Looked at the "Administrators" page heading
- **What I expected**: Same font and size as the "Search Registrations" header
- **What happened**: Heading is too large — should be consistent with other page headers
- **Severity**: UX
- **Status**: Deferred
- **Note**: Refer to PL-022 — heading sizing folds into the broader "all Configure pages use the narrow Nav Editor workspace" fix. Resolve together.

### PL-001: Administrators table — match Search/Player table style and reorder columns
- **Area**: Menu Display
- **What I did**: Opened the Administrators menu and looked at the table
- **What I expected**: Consistent look with the Search/Player menu table
- **What happened**: Needs several changes: (1) Match column heading font and style to Search/Player table, (2) Change "Status" to "Active" and move it right after the Name column with "Yes" if active, (3) Column order should be: Name, Active, Role, Username, Registered
- **Severity**: UX
- **Status**: Deferred
