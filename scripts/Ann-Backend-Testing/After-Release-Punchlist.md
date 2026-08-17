# After-Release Punchlist

Items intentionally deferred to **after go-live** — enhancements, non-blocking polish, and anything that can only be exercised/verified once we're live in Production. Sibling to `Admin-Menus-Punchlist.md` (`AM-`) and `Payment-Test-Punchlist.md` (`PL-`); those two stay scoped to the pre-release verification pass.

- **Item IDs:** `AR-###`, numbered oldest → newest (newest at the bottom). The `AR-` prefix keeps this queue distinct from `AM-` / `PL-` — no number collisions, and Todd can grep `AR-` for the post-launch backlog.
- **Each item** carries Ann's request and, where known, a Claude-verified root cause (code + DB) and clear **For Todd** directions.
- **Nothing here blocks go-live.** If an item turns out to be release-critical, promote it to `AM-`/`PL-` and note the move.

---

### Marker legend (same as the other punchlists)
- `✅ VERIFIED PASSING (Ann, MM-DD)` — Ann retested a fix, confirmed good
- `🟡 FIXED (Todd, MM-DD) — awaiting Ann verify` — fix shipped, queued for Ann's next-pass retest
- `🔴 STILL OPEN` / `🔴 RE-OPENED` — not yet fixed / regressed
- `✅ Acknowledged (Ann, MM-DD)` — Ann signed off on a decision (Won't Fix / by-design), no code change
- `Won't Fix` — decision recorded with rationale
- `🔵 REVISIT` — parked feature/enhancement, circle back

---

<!-- New items go below this line, newest at the bottom, next id = AR-012 -->

### AR-011: [Payments / Add Accounting Record] The Amount field cannot be cleared — a `0` reappears under the caret
- **Topic**: Payments → registrant/team fly-in → **Add Accounting Record** → AMOUNT field (Credit Card / Check / Correction)
- **Split out of AR-009 (08-17)**, which was the *disappearing modal* and is fixed. This is the other half of Ann's original write-up — the part about deleting the entry to retype it.
- **What the admin sees**: Amount shows `900`. Backspacing to retype gives `90`, `9`, then the moment the field goes empty **a `0` appears on its own** with the caret after it, so the new number is typed beside it and the field reads `0900`. The field fights the user.
- **Root cause (verified, Claude 08-17)**: one-way binding `[ngModel]="amount()"` + `(ngModelChange)="setAmount($event)"`, and `setAmount` coerces with `value ?? 0`. An empty number input emits `null` → stored as `0` → the signal now disagrees with the empty box, so Angular writes `0` back into it. The signal has no way to represent "empty".
- **NOT a money risk — deliberately deferred on that basis (Todd, 08-17).** `canSubmitPayment()` already refuses zero on every tab (CC needs `amt > 0`; correction returns false at `0`; check falls through to `amt !== 0`), so Save is disabled and a `$0` record cannot be booked. `0900` also parses to `900`, so even the ugly display submits the correct number. The failure mode is confusion and re-typing, not a wrong ledger amount.
- **Fix when we take it**: let the amount signal hold `null` while the field is empty and let the existing submit guard treat `null` as not-submittable. Two lines, one component, no backend, no DTO. **Money-entry binding — give it its own deliberate pass and its own verification; do NOT ride it along behind an unrelated fix.**
- **Severity**: UX friction on a money form (no incorrect-amount path)
- **Status**: 🔵 REVISIT — parked by Todd 08-17. Trigger to un-park: Ann re-reports the field fighting her after the AR-009 backdrop fix lands.

### AR-010: [Payments / ARB] Update CC for ARB subscribers — confirm it works, incl. behind-in-payments catch-up (legacy parity)
- **Topic**: Payments → ARB subscriptions → **Update CC** menu
- **Context**: This is a function we **couldn't test before but can now**. In **Legacy**, when an ARB registrant appeared under **"Look/Fall Behind in Payment"** (ARB Subscription Health → behind-in-payments list), going to the **Update CC** menu let them **make that overdue payment** at the same time — and they could **re-enter the same card** as before and still make the payment.
- **Verify — all 3 scenarios**:
  1. **Not behind in payments** → Update CC only (just swaps the card on the subscription, no charge).
  2. **Behind in payments** → Update CC **and make the past-due payment at the same time** (new card).
  3. **Behind in payments** → enter the **same CC** as before **and still make the payment** (legacy allowed re-using the identical card to catch up — confirm the new system doesn't reject "same card" and block the catch-up).
- **Test data**: **`YJ North Players 2026-2027`** currently has players who are **Behind in Payments** — use it for scenarios 2 & 3.
- **For Todd / testing**: confirm the Update CC flow (a) succeeds for the not-behind case, (b) offers/executes the catch-up charge for behind subscribers, and (c) permits the same-card re-entry catch-up. Flag if any scenario errors or silently fails to charge.
- **Severity**: Legacy-parity verification (test now that ARB behind-payment data exists)
- **Status**: 🔴 OPEN — to test (Ann / Todd)

### AR-009: [Payments / Add Accounting Record] Deleting the Amount value boots you out of the popup
- **Topic**: Payments → registrant/team fly-in → **Add Accounting Record** popup → Amount field
- **Where**: the fly-in for a **Player**, a **Team Club Rep**, or a **Team** → **Add Accounting Record** modal (Credit Card / Check / Correction tabs).
- **Repro (Ann)**: In the **AMOUNT** field, place the cursor at the **end** of an existing number and **delete the entry** (backspace) to type a different amount → the **modal closes / boots you out** unexpectedly. See image (Check tab, Amount `900` being edited, arrow on the field).
- **Likely cause (to verify, Claude)**: a **Backspace/Delete keydown on the (now-empty) Amount input is bubbling to a modal-level handler** — either an Escape/close key binding, a `keydown` that treats empty-field backspace as "back/dismiss," or the value going empty/`NaN` tripping a guard that dismisses the dialog. Classic fix: `stopPropagation` on the input's keydown (or don't close the modal on backspace/empty), and treat an empty Amount as `0`/invalid inline rather than closing.
- **For Todd**: Amount input in the Add Accounting Record modal must let the user clear/retype freely — backspace at field start/end should never close the dialog. Confirm no `@keydown`/host listener is catching Backspace, and that clearing the field doesn't trigger a close/validation-dismiss.
- **Severity**: Bug / data entry (annoying + risks losing entered check number/comment on close)
- **ROOT CAUSE — the backdrop, not the keystroke (Todd's lead, 08-17).** The filed hypothesis is wrong and worth recording as wrong: **no Backspace handler exists anywhere in the frontend** except the unrelated score-entry modal, and modal visibility has only four inbound close paths (backdrop, Escape, X, submit) — a keystroke reaches none of them. Todd's observation that clicking outside the modal dismisses it is the actual mechanism, and it was by design: the overlay carried `(click)="closePaymentModal()"`.
  - **Why a mouse wobble is enough, and why Ann's box but not Todd's:** a `click` event fires on the **nearest common ancestor of the mousedown and mouseup targets**. Press inside the card, release a few pixels outside it, and the browser dispatches `click` on the *overlay* — so the modal closes even though the press began inside it. Triggers: pointer drift (Bluetooth jitter, low battery, trackpad), clicking near the card's edge, or **`appDraggableModal` sliding the card out from under a stationary cursor**. Intermittent and hardware-sensitive by nature — which is exactly why it wouldn't reproduce on a different machine. Her mouse is the aggravating factor; the code is what turned a 3-pixel wobble into lost data entry.
- **FIXED (Claude, 08-17) — awaiting Ann verify.** Backdrop dismissal now requires the press **and** the release to land on the backdrop itself (`(mousedown)`/`(mouseup)` pair + `event.target === event.currentTarget` on both). Press-inside/release-outside and press-outside/release-inside both fall through and do nothing. Escape, X, Cancel and submit unchanged; click-away still works when it's genuinely a click away. Applies to the **CC Refund** modal too, which shares the overlay — correct, not collateral. `npx tsc --noEmit` clean.
  - Chose this over removing backdrop-close outright (also offered): it kills the failure mode without taking away a working affordance, and it holds up if her mouse is replaced tomorrow.
- **FOUND, NOT FIXED — the Amount field cannot be cleared.** `[ngModel]="amount()"` + `setAmount()` doing `value ?? 0` means deleting the last digit emits `null` → stores `0` → Angular writes `0` straight back under the caret. So "delete the entry and type a different amount" literally fights the user. Independent of the backdrop bug, and it IS the action Ann described. **Needs a go — not built.**
- **NOT SWEPT**: the same `(click)`-on-overlay backdrop pattern likely appears on other modals app-wide. Deliberately left for its own item rather than smuggled into this fix.
- **Status**: ✅ VERIFIED PASSING (Ann, 08-17) — **CLOSED.** She is no longer getting the random disappearance (Todd relayed). The press-and-release requirement holds on the machine the bug was native to, which is the only box that could have proved it. Un-park AR-011 only if she re-reports the Amount field itself fighting her.

### AR-008: [Coach Registrations / Email] Direct-placement Staff confirmation email shows no teams — "Teams you will be rostered with:" is blank on tournaments/leagues
- **Topic**: Coach Registrations → confirmation email → team list token (`!STAFFCHOICES` / `!F-TEAMS`)
- **Applies to**: **Direct-to-roster** coach type (Tournament/League Staff). **Works correctly** for the Club funnel (coach → Unassigned Adult).
- **Observation (Ann)**: On tournament/league coach confirmations, the **"Teams you will be rostered with:"** section is **empty** — the staff-teams token doesn't render the assigned teams. See image: "Ann Massey … registered as a Coach for: Long Island Elite Lacrosse:Prime Time Recruiting Showcase 2026 / Teams you will be rostered with:" then **blank** before the Contacts table. The same token **does** populate for coaches who register as Unassigned Adults.
- **Hypothesis to verify (Claude)**: the two regimes store the team selection **differently**, and the token resolver likely reads the UA shape:
  - **Unassigned Adult (Club):** selections are non-binding **REQUESTS** written into **`SpecialRequests`** (structured JSON) — the token reads from there ✅.
  - **Direct-placement Staff (Tournament/League):** `AllowTeamRequests:false`, so **nothing is written to `SpecialRequests`**; instead one **Staff `Registration` per team** is minted with **`AssignedTeamId`** set (per `e6d3bd08`). If the confirmation token resolves off `SpecialRequests` (or off a single reg's AssignedTeamId) rather than aggregating the **per-team Staff rows**, it comes back empty ❌.
  - Note there IS a `BuildStaffTeamsHtmlAsync` path that replaces `!F-TEAMS` by querying active Staff rows by user+job — worth checking whether (a) the confirmation for direct placement actually goes through that path, and (b) it runs **after** all per-team Staff rows are committed (a timing gap would also yield empty).
- **For Todd**: make the tournament/league Staff confirmation aggregate the **per-team Staff Registrations (AssignedTeamId → team)** for this user+job and render them in the "rostered with" list, matching the UA behavior. Confirm which token the template uses (`!STAFFCHOICES` vs `!F-TEAMS`).
- **Severity**: Bug / confirmation correctness (non-blocking, but the coach gets an email that omits their teams)
- **ROOT CAUSE CONFIRMED (Claude, code + data, 08-17)** — the token is **`!F-STAFFCHOICES`**, and it resolved **only** off `Registrations.specialRequests`:
  - The job's `CoachReg_ConfirmationEmail` on `lielite-primetimerecruitingshowcase-2026` reads `...Teams you will be rostered with:<br><br>!F-STAFFCHOICES...` — the heading is **director-authored per job**, not ours.
  - `BuildStaffChoicesAsync` parsed the codified `{teams:[…]}` request JSON. Direct placement sets `AllowTeamRequests: false` and **never writes that column** (per `e6d3bd08`) — the teams live in one Staff row per team with `assigned_teamID` set. No requested ids → `string.Empty` → heading over a blank. Verified against live rows on that job: `assigned_teamID` **set**, `specialRequests` **NULL/empty**, on every Staff row.
  - **Not a timing gap** and **not** `BuildStaffTeamsHtmlAsync` — that path only serves `!F-TEAMS` and already aggregates the Staff rows correctly. The bug is that the *other* team token was never taught the second regime.
  - **Scope, by effective coach template (dev-restore DB):** Tournament (2) — **144 jobs on `!F-STAFFCHOICES`** (broken) vs 212 on `!F-TEAMS` (already correct). League (3) — **12** vs 3. That per-job template split is why the symptom isn't universal.
- **FIXED (Claude, 08-17) — awaiting Ann verify.** Fixed in the **shared resolver** (Todd's pick over pre-rendering at the adult chokepoint, so every consumer of the token benefits, not just the adult wizard):
  - `BuildStaffChoicesAsync` — when SpecialRequests yields no requested ids, fall back to `GetCoachTeamChoicesAsync` (every **active** Staff row for this user+job, one per team) and render the same `Club: Age: Team` list. **UA path byte-identical** — with requested ids present nothing changes. Dedupes on the label: the email lists teams, not rows.
  - `GetCoachTeamChoicesAsync` — two latent defects hardened in the same pass, both live only now that real confirmations route through it: added the **`bActive` filter** (a coach removed from a team must not see it on a resend), and the club-rep join for the club name is now a **left join** with a null-safe projection (that FK is nullable — a rostered team must never drop out of a confirmation for lacking a club rep; mirrors `GetTeamLabelsByIdsAsync`).
  - Backend only — no DTO change, no API-model regen. `dotnet build` green.
- **Rulings recorded**: coaches already mailed a blank list get **no resend and no backfill** (Todd) · the *"Teams you will be rostered with:"* heading **stays as authored** (Todd). Noted but NOT actioned: the same token on a **club** site renders *requested* teams under a heading promising placement — a per-job copy mismatch across ~323 club templates, a separate item if it ever matters.
- **Status**: 🟡 FIXED (08-17) — awaiting Ann verify: register as Coach on a tournament/league job with 2+ teams, confirm the screen AND the email both list every team; then a club-site coach still shows "Requested Teams (pending director approval)" unchanged.

### AR-007: [Communications / Smart Bulletins] Allow SuperUser (and consider Director) to inactivate a Smart Bulletin
- **Topic**: Communications → Smart Bulletins (the auto-generated registration banner / quicklinks)
- **Request (Ann)**: Give **SuperUser** the ability to **inactivate (turn off) a Smart Bulletin** when they don't want it shown. **Consider extending the same option to Directors.**
- **Why (Ann's use case)**: A director may prefer a **customized, info-only banner** (announcement text without the registration quicklinks), or may **not want the Smart Bulletin to show at all**. Right now there's no way to suppress it, so they can't substitute their own banner or hide it.
- **For Todd — decisions**:
  1. **Scope** — SuperUser-only first, or SuperUser + Director? (Ann leans toward offering it to Directors too.)
  2. **Granularity** — a simple **on/off (active/inactive) toggle** per Smart Bulletin, vs. an "info-only" mode that keeps the banner but strips the registration quicklinks. Ann's phrasing suggests both are desirable (turn off entirely *or* show info without quicklinks).
  3. Confirm what "Smart Bulletin" maps to in the current model (auto-generated vs. director-authored bulletin) so the toggle lands on the right entity.
- **Severity**: Feature / director control (non-blocking)
- **Status**: 🔴 OPEN — for Todd

### AR-006: [LADT] Surface "Self Roster" state in LADT — Age Group column + info buttons; clarify vs Team "Active"
- **Topic**: LADT (League / Age-Group / Division / Team editor) → self-rostering visibility
- **Request (Ann)**:
  1. **Age Group level** — add a **SELF ROSTER** column (checked / unchecked) so a director can see at a glance whether self-rostering is turned on for that age group.
  2. **Team level clarity** — the Team editor shows **Active**, but it's ambiguous: does **Active** mean self-rostering is on, or is **Self Rostering a separate toggle** that also has to be turned on? Make the distinction explicit.
  3. **Info buttons on both levels** — Ann assumes self-roster is **hierarchical** (set at a higher level, inherited down, like the payment-phase cascade). Add an **info (ℹ) button at both the Age Group and Team levels** noting **where the setting is actually controlled** (which level owns it, what's inherited).
- **For Todd — confirm the model first**: pin down the real self-roster flag(s) and their hierarchy before building the UI — is there an age-group-level self-roster flag, a team-level one, both, and how do they cascade? **⚠ Roster-visibility semantics were just changed in `e6d3bd08` (consent-gated `BAllowRosterViewAdult`)** — confirm whether "self roster" here is that same flag or a distinct player-self-rostering toggle, so the new column/info copy describes the right thing. Use design-system info-button pattern; no hardcoded colors.
- **⚠ Constraint (Ann)**: adding the SELF ROSTER column **must NOT introduce horizontal scrolling** on the Age Group grid. Fit it within the existing width (tighten/absorb other columns, use a compact checkbox/icon cell) — no h-scroll.
- **Severity**: UI clarity / director usability (non-blocking)
- **Status**: 🔴 OPEN — for Todd (confirm flag hierarchy, then add column + info buttons)

### AR-005: [Coach Registrations / Security] SuperUser login was able to self-register as a Coach — intended?
- **Topic**: Coach Registrations → role-lane purity
- **Observation (Ann)**: Using the **TSIC SuperUser login**, Ann was able to **self-register as a Coach** on a job. Flagging because it was unexpected.
- **Question for Todd**: Is this intended? Two angles:
  1. **Testing convenience** — SuperUser is deliberately exempt from most gates (cross-job ops), so being able to register-as-anything may be by design for testing.
  2. **Lane-purity risk** — but a real Coach/Staff registration on a SuperUser account writes a live role grant. Per the AM-004 lane model, only LIVE registrations classify an account's eligibility; a stray Staff row on the SuperUser account could muddy that, or surface the SuperUser in coach/roster views. Confirm this doesn't poison the SuperUser's lane or leak it into director-facing lists.
- **For Todd**: decide whether SuperUser should be **blocked** from self-registering as a Coach (clean-lane), or **allowed** (testing) — and if allowed, confirm the resulting Staff row is filtered out of coach/roster/approval surfaces the way other admin lanes are.
- **Severity**: Question / possible security-lane concern (non-blocking; verify before go-live if it affects real accounts)
- **Status**: 🔴 OPEN — awaiting Todd's decision

### AR-004: [Rosters / PDF] Roster PDF truncates the Position column — "Midfield" prints as "Midfiel"
- **Topic**: Rosters → PDF export → Position column
- **Observation (Ann)**: In the generated **roster PDF**, the **Position** value is **cut off** — e.g. "Midfield" renders as "Midfiel" (last character clipped). The column is too narrow / the cell text is being truncated rather than fit.
- **For Todd**: widen the Position column (or shrink/auto-fit the text) in the roster PDF generator so full position names print. Check the longest expected values (Midfield, Attack, Defense, Goalie, Long Stick Midfield / LSM, etc.) so the fix holds for the widest label, not just "Midfield". Likely a fixed column width or a text-clip in the PDF layout, not a data problem (the stored value is complete).
- **Severity**: Bug / output correctness (non-blocking; cosmetic-but-wrong on a printed artifact)
- **Status**: 🔴 OPEN — for Todd

### AR-003: [Coach Registrations] "Teams You're Coaching" list is hard to read — stack it vertically with a light (blue?) badge per team
- **Topic**: Coach Registrations → adult registration wizard → "Teams You're Coaching" review section (direct-placement Staff on Tournament/League)
- **Applies to**: **Direct-to-roster** coach type (tournaments, leagues) — the step showing the teams the coach will be placed on, above the "Submitting registers you as staff on **every team listed above**" callout.
- **Request (Ann)**: The teams listed under **"TEAMS YOU'RE COACHING"** are **not easily seen** — they currently run **left-to-right inline** on one line, which reads as a run-on. Make each team stand out:
  1. Render as a **vertical list** (one team per line, not a horizontal left→right row).
  2. Give each team a **light badge** (blue suggested) so they're visually distinct and scannable.
  - See attached image: two teams shown inline (`3 Point:2028:Unassigned:3 Point 2028   214LAXDALLAS:2028:Unassigned:214LAXDALLAS 2028`) with the amber "every team listed above" callout beneath.
- **For Todd**: this is the direct-placement review list (the one gated by the server-derived `DirectPlacement` flag from `e6d3bd08`). Use design-system tokens for the badge (no hardcoded blue — e.g. `.bg-primary-subtle` / a subtle info badge), keep the amber callout as-is.
- **Severity**: UI / readability (non-blocking)
- **FIXED (Claude, 08-17) — awaiting Ann verify.** CSS only, three declarations on `.teams-wrap` in `review-step.component.ts`: `flex-direction: column` + `align-items: flex-start` (each pill hugs its own text rather than stretching the column) + gap `--space-1` → `--space-2`. No template, binding, logic, or colour change.
  - **The badge Ann asked for already existed** — `.review-team-pill` is already primary-tinted background + primary border + primary text, all token-derived. The pills simply weren't stacked: `flex-wrap: wrap` at a 4px gap put two long `Club:Age:Division:Team` labels side by side, which is the run-on she saw. Nothing was added for the badge.
  - **Shape precedent:** the Profile step one screen earlier already renders this same selection as a vertical one-per-line list — Review was the odd one out. Matched its *arrangement*, kept Review's *styling*; did NOT convert to Profile's `<ul>`, which would be a template change and would drop the badge.
  - **Both faces of the section move, intended:** direct-placement "Teams You're Coaching" and the club-site "Teams Requested" `@else` branch share `.teams-wrap`. Both were inline; both now stack.
  - **Risk:** nil. Angular view encapsulation scopes the rule to this component; `.teams-wrap` exists nowhere else (the class *name* `.review-team-pill` is also used by the player review step, but as an independent scoped copy — untouched either way). No colour declarations, so every palette and both light/dark render as before. No animation, so nothing for reduced-motion. `npx tsc --noEmit` clean.
- **NOT DONE, deliberately — the label noise.** `3 Point:2028:Unassigned:3 Point 2028` repeats the club and year and leaks the literal **"Unassigned"** division placeholder into customer-facing text. It comes from `availableTeams().displayText`, the SAME value feeding the ej2 team picker one step earlier. Rejected both fixes: a Review-only transform would print one team under two different names three clicks apart in one wizard (worse than a noisy label), and changing `displayText` at the source moves the picker — not no-risk, and not what Ann filed. **File separately if the placeholder leak matters.**
- **Status**: 🟡 FIXED (08-17) — awaiting Ann verify: tournament/league coach with 2+ teams, Review step shows one badged team per line, amber callout unchanged; club-site coach's "Teams Requested" list stacks the same way.

### AR-002: [Coach Registrations] Tournament/league coaches register directly as Staff (never Unassigned Adult) — confirm behavior + clean up residual Unassigned Adults
- **Topic**: Coach Registrations → Staff vs Unassigned Adult
- **Applies to**: **Direct-to-roster** coach type (tournaments, leagues). Ann believes this pertains to the **upcoming Fall tournaments** specifically.
- **Observation (Ann)**: On tournament/league sites, a Coach is registered **directly as Staff** and **never passes through the Unassigned Adult state**.
- **Confirm (for Todd)**: Is this the intended behavior — direct-to-Staff, no Unassigned Adult step?
- **If confirmed, two follow-ups**:
  1. **Director-account creation** — does this mean tournament/league sites **cannot be used to create a Director account** (since a Director account path presumably runs through the Unassigned Adult → elevate flow)? Confirm true/false. *(Ties to AR-001's "register as Coach en route to Director" case — if direct-to-Staff is the rule, that AR-001 rationale may not hold.)*
  2. **Residual cleanup** — there are **leftover Unassigned Adult registrations from before the release** on these jobs. Were they all **approved**? If so, should we **delete the now-distinct Unassigned Adult registrations** so directors aren't confused by duplicate/orphaned adult rows?
- **Severity**: Question + data cleanup (non-blocking; verify before deleting anything)
- **Data check available**: Claude can enumerate the residual Unassigned Adult registrations on the upcoming Fall tournament jobs (count + per-job breakdown) so the cleanup decision is made against real numbers, not an estimate.
- **CONFIRMED (Claude, code + data, 2026-08-14 code):** The direct-to-Staff behavior is **intended and current**. Todd's commit `e6d3bd08` ("two-regime coach registration") makes Tournament/League coach self-reg resolve to **`RoleConstants.Staff`** with **one Staff Registration per selected team** (binding, no approval, no UnassignedAdult row). Club (player-reg) sites keep the UnassignedAdult funnel + Roster Swapper approval. Live data corroborates: Fall 2026 tournaments are Staff-only, zero UnassignedAdult (e.g. `Karenpequa@aol.com` = 7 Staff rows minted in the same instant = one per team).
  - **So follow-up #1 (Director-account path):** on Tournament/League there is no UnassignedAdult step at all, which reinforces AR-001's question — a coach on these sites lands as Staff, not as an elevatable Unassigned Adult. Todd to confirm whether the "register as Coach → become Director" path is expected to exist on team-reg sites (it may only exist on Club sites).
  - **So follow-up #2 (residual cleanup) — two DISTINCT cases:**
    - **Club sites:** already handled — commit `75d82744` **deletes the UnassignedAdult row when a director grants a pending coach**, so no residual accumulates going forward.
    - **Tournament/League sites:** the grant-delete does **not** apply (there's no grant step here — coaches are placed directly). Any UnassignedAdult rows on these jobs are **pre-release residue** from the old universal-funnel behavior, orphaned by the regime switch. **This is the case that still needs a one-time cleanup decision** — enumerate + confirm none are legitimately in-flight, then delete so directors don't see phantom Unassigned Adults next to the real Staff placements.
- **Status**: 🔴 OPEN — behavior CONFIRMED; still needs (a) Todd's ruling on the Director-account path per site type, and (b) the one-time tournament/league residual-UA cleanup decision (Club side already covered by `75d82744`)

### AR-001: [Coach Registrations] Coach Approval menu still shows for tournaments/leagues (direct-to-roster) — keep or hide?
- **Topic**: Coach Registrations → Coach Approval menu
- **Applies to**: **Direct-to-roster** coach type (tournaments, leagues) — where coaches are placed straight onto the team roster and the approval step isn't used.
- **Observation (Ann)**: The **Coach Approval** menu is still present for tournament/league sites, even though those sites place coaches directly on rosters and won't use the approval workflow.
- **Ann's leaning**: Initially thought we should **hide it** on these site types. But there may be a legitimate reason to keep it: someone could register as a **Coach** on this type of site **as part of the process of being added as a Director** — in which case the approval menu could still be useful.
- **Question for Todd**: Should the Coach Approval menu be **hidden** on direct-to-roster (tournament/league) sites, or **left visible** to support the "register as Coach en route to Director" case? Decision his call.
- **Severity**: Question / product decision (non-blocking)
- **Status**: 🔴 OPEN — awaiting Todd's decision
