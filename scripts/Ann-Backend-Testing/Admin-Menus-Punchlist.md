# Admin Menus Punchlist

Ann's review of **Director / SuperUser menu functions** (and other admin-side items that surface during it). Sibling to `Payment-Test-Punchlist.md`, which stays scoped to registration/payment.

- **Item IDs:** `AM-###`, numbered oldest → newest (newest at the bottom). The `AM-` prefix keeps this queue distinct from the payment doc's `PL-` — Todd greps `AM-` for admin, `PL-` for payment, no number collisions.
- **Each item** carries a Claude-verified root cause (code + DB where relevant) and clear **For Todd** directions.
- **Cross-topic:** if an admin finding actually belongs to another area, it still lives here (with a topic tag) so Todd sees it in this review.

### Marker legend (same as the payment punchlist)
- `✅ VERIFIED PASSING (Ann, MM-DD)` — Ann retested a fix, confirmed good
- `🟡 FIXED (Todd, MM-DD) — awaiting Ann verify` — fix shipped, queued for Ann's next-pass retest
- `🔴 STILL OPEN` / `🔴 RE-OPENED` — not yet fixed / regressed
- `✅ Acknowledged (Ann, MM-DD)` — Ann signed off on a decision (Won't Fix / by-design), no code change
- `Won't Fix` — decision recorded with rationale
- `🔵 REVISIT` — parked feature/enhancement, circle back

---

### AM-001: [Communications / Bulletins] The bulletin rich-text editor exposes far fewer options than Legacy — enrich the Syncfusion toolbar to match (within brand guardrails)
- **Topic**: Communications → Bulletins editor
- **Tested**: Bulletins → create/edit bulletin → the rich-text editor
- **Request (Ann)**: The bulletin editor has **far fewer formatting options than the Legacy bulletin editor**. Can Syncfusion meet the function we had previously? — and if so, enrich it.
- **Answer (verified): Yes — comfortably. The limit is *configuration*, not a Syncfusion capability ceiling.**
  - Every RTE in the app (bulletins, job-config HTML fields, the help editor) shares **one deliberately-minimal toolbar** config, `JOB_CONFIG_RTE_TOOLS` ([rte-config.ts:2-9](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/shared/rte-config.ts#L2)), exposing only **10 tools**: Bold · Italic · Underline · FontColor · BackgroundColor · OrderedList · UnorderedList · CreateLink · Undo · Redo. No font family/size, headings, alignment, indent, tables, images, or source view. Consumed by [bulletin-form-modal.component.ts:400](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/communications/bulletins/components/bulletin-form-modal.component.ts#L400) (and the help editor, job-config tabs).
  - **Syncfusion RichTextEditor supports far more, all built-in** (just not enabled): **Text** — FontName, FontSize, Formats (Paragraph / H1–H6 / Quote / Code), StrikeThrough, SuperScript, SubScript, ClearFormat; **Layout** — Alignments (L/C/R/Justify), Outdent/Indent, HorizontalLine; **Insert** — CreateTable (Table module), Image, Audio/Video, EmojiPicker; **Power-user** — SourceCode (edit raw HTML), FullScreen, Print, PasteCleanup. This matches or exceeds the Legacy editor.
- **Severity**: Feature / Legacy-parity gap
- **Status**: Open (Ann, 2026-07-26)
- **The real decision is a product/brand one, not technical**: the minimal toolbar was almost certainly a **deliberate brand-safety choice** — fewer fonts/colors/sizes means a director can't produce off-brand content. This is the exact tension **PL-043-T** (payment punchlist) already flags: *"more expressive range (tables, sectioning) without regaining the ability to break brand."* So the question for Todd is **which tools to allow back in**, and how to **constrain styles** so the added power doesn't reopen brand-break risk.
- **For Todd — the change**:
  1. **Expand the toolbar** — add the wanted tools to the `items` list and enable the needed modules (e.g. **Table**, **Image**). Reaching Legacy parity is a config change; no new dependency.
  2. **Constrain, don't just open** — pair it with **PasteCleanup** + an allowed formats/colors policy so directors gain tables/sectioning without off-brand fonts/colors (the PL-043-T guardrail).
  3. **Shared-config caution** — `JOB_CONFIG_RTE_TOOLS` is shared across **bulletins, job-config HTML fields, and the help editor**, so widening it hits all three. Decide whether to **enrich the shared config** or give **bulletins its own richer config** (leaving job-config/help minimal).
  - **Cross-ref**: PL-057 #5 (payment punchlist) asks to add an RTE to the **ARB defensive email** (today a bare textarea); it should **reuse whatever toolbar config lands here** so the editing experience is consistent.

### AM-002: [Configure / Administrators] Administrators table — match Search/Player table style and reorder columns
- **Topic**: Configure Menus → Administrators table
- **Source**: Carried forward from ConfigureMenus punchlist PL-001 (consolidation sweep, 2026-07-26)
- **Tested**: Configure → Administrators menu → the table
- **Request (Ann)**: Make the Administrators table consistent with the Search/Player menu table:
  1. Match column heading font and style to the Search/Player table.
  2. Rename the "Status" column to "Active" and move it immediately after Name, showing "Yes" when active.
  3. Column order should be: **Name, Active, Role, Username, Registered**.
  4. Compress the table — rows and columns are too widely spaced; tighten overall (folded in from ConfigureMenus PL-004).
- **Severity**: UX
- **Status**: Open (Ann, 2026-07-26)

### AM-003: [Configure / Administrators] Primary-contact star — reposition, and carry it forward on clone + legacy migration
- **Topic**: Configure Menus → Administrators table (primary-contact star)
- **Source**: Carried forward from ConfigureMenus punchlist PL-005 (sub-points 1, 3, 5) (consolidation sweep, 2026-07-26)
- **Tested**: Configure → Administrators → the star icons that set a job's primary contact
- **Request (Ann)** — three related fixes to make the primary-contact star behave correctly:
  1. **Reposition the star** — move it to the **left of the Director's name** for easier scanning (today it sits in the far-right Actions column).
  2. **Carry forward on job clone** — a cloned job currently lands with **no** primary contact. `JobCloneService` copies admin registrations but never copies `Jobs.PrimaryContactRegistrationId`; it must carry the primary contact through to the clone.
  3. **Legacy migration backfill (REQUIRED)** — the migration must set `Jobs.PrimaryContactRegistrationId` from Legacy's equivalent field so the star lands on the Director who was the primary contact in Legacy, preserving customer continuity. Eyeball a handful of migrated jobs to confirm.
- **Severity**: UX / Bug (clone + migration carry-forward)
- **Status**: Open (Ann, 2026-07-26)

### AM-004: [Configure / Administrators] 🔴 SECURITY — "Add Administrator" user search is unscoped platform-wide (cross-customer privilege escalation)
- **Topic**: Configure Menus → Administrators → Add Administrator (username search)
- **Source**: Carried forward from ConfigureMenus punchlist PL-006 (consolidation sweep, 2026-07-26)
- **🔴 SECURITY ISSUE (verified in code, 2026-07-26)**: The Add-Administrator username search is **unscoped** — no customer filter, no role filter. A Director at **Customer A can add any user from Customer B** (or any account on the platform) as a Director of their own job. This is a **cross-customer / cross-tenant privilege-escalation and PII-exposure path**, not just a UX annoyance. Treat as the priority part of this item.
- **Tested**: Configure → Administrators → "Add Administrator" → Username search dropdown
- **Verified current behavior (code-traced)**:
  - `AdministratorService.SearchUsersAsync` → `UserRepository.SearchAsync` ([UserRepository.cs:166-190](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/UserRepository.cs#L166)) queries **every row in `AspNetUsers`**, case-insensitive `Contains` on UserName / FirstName / LastName, ordered by name, **top 10**. No role filter. No customer/job scope.
  - `AddAdministratorAsync` ([AdministratorService.cs:51-90](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/AdministratorService.cs#L51)) then writes a `Registrations` row for the **current job** with `RegistrationCategory="Director"` + chosen role + `BActive=true`, with **no check** that the selected user has any prior association with the current customer.
  - Effective eligibility rule today = *"has any account anywhere on the TSIC platform."* Parents, players, and unrelated-customer staff all intermix; the "random" feel Ann reported is the arbitrary top-10 alphabetical slice of a loose match.
- **Request (Ann)**: Make it clear who is eligible to be added, and stop surfacing random / unrelated users.
- **For Todd — the fix (pick a scoping model)**:
  - **A. Scope to same customer** — only users with an existing Registration under a job owned by the current customer.
  - **B. Platform-wide but role-filtered** — require ≥1 Director/SuperDirector/Superuser role somewhere; exclude parent/player-only accounts.
  - **C. Leave wide, add role/customer label per row** — weakest; does not close the leakage.
  - **D. Default A + explicit "search all users" toggle** — for the rare legitimate cross-customer add.
  - **Recommendation**: **A or D** — either eliminates the random feel *and* closes the cross-customer add. Enforce the scope **server-side** in the repository query, not just in the UI.
- **Severity**: 🔴 Security (cross-tenant privilege escalation) + UX
- **Status**: Open (Ann, 2026-07-26)

### AM-005: [Configure / Customer Groups] SuperUser-only screen — overall styling can be tighter
- **Topic**: Configure Menus → Customer Groups (SuperUser-only)
- **Source**: Carried forward from ConfigureMenus punchlist PL-011 / PL-012 / PL-013 (consolidation sweep, 2026-07-26)
- **Framing**: An example of overall styling that can be tighter on this screen. Three cosmetic items grouped:
  1. **Remove the Groups number badge** — drop the total-groups count shown at the top.
  2. **"Members of '[group name]'" header needs visual emphasis** — the group name is flat inline text; split it out and style it distinctly (bold / accent / pill).
  3. **Add and Delete buttons too far from customer names** — the per-row Delete (×) is pinned to the far right; move it next to the customer name so the controls sit close to what they act on.
- **Severity**: UX
- **Status**: Open (Ann, 2026-07-26)

### AM-006: [Configure / Discount Codes + all Configure tables] Blocked-delete needs a lock icon, and an Expired code must not read "Active"
- **Topic**: Configure Menus → Discount Codes (and any Configure table with conditional delete)
- **Source**: Carried forward from ConfigureMenus punchlist PL-026 + PL-028 (consolidation sweep, 2026-07-26)
- **Two related Discount-Codes findings:**
  1. **Blocked delete shows nothing (PL-026)** — when a row can't be deleted (e.g. a discount code with `usageCount > 0`), the trash icon is simply **omitted**, leaving an empty action slot with no explanation. Ann wants a **lock icon** where the trash would go, with a hover tooltip saying **why** ("Cannot remove because the code has been used"). Same guard pattern exists on **Customers** (trash hidden when `jobCount > 0`) and **Administrators** — so make it a **consistent app-wide treatment** (one shared `.icon-btn-locked` style + per-surface tooltip wording), not a one-off.
  2. **A code can read "Active" while Expired — oxymoron (PL-028)** — the Discount Codes grid shows two columns that collide on the word "Active" with different meanings, and an expired code can display **Expiry: "Expired"** and **Status: "Active"** at the same time:
     - **Expiry** (`getExpirationText`): "Expired" when past end date, "Nd left" within 7 days, else **"Active"**.
     - **Status** (`isActive`): **"Active"** when the enable toggle is on, else "Inactive" — it never checks expiry, so an expired code stays "Active" until someone manually flips the toggle.
     - **Fix**: (a) rename Expiry's happy-case "Active" to a date-based word ("Valid" / "In date" / show the end date), and (b) make Status a **computed** value — `isExpired ? 'Inactive' : (isActive ? 'Active' : 'Inactive')` — so an expired code can never read "Active." Expiry answers *when*; Status answers *working now*.
     - **Backend check before shipping**: confirm nothing downstream treats `isActive === true` as "usable right now" without also checking `isExpired`; if it does, tighten those call sites or expose the `!isExpired && isActive` derivation on the DTO.
- **Severity**: UX + Bug (stale/incorrect Active status)
- **Status**: Open (Ann, 2026-07-26)

### AM-007: [Configure / Dropdown Options] Make value chips drag-reorderable
- **Topic**: Configure Menus → Dropdown Options (SuperUser-only)
- **Source**: Carried forward from ConfigureMenus punchlist PL-030 (consolidation sweep, 2026-07-26)
- **Request (Ann)**: Value chips for a category (e.g. Jersey Sizes, Shorts Sizes) render in insertion order, and the only mutations are add (appends to end) and remove — there's no way to fix the order without delete/re-add. Let users **drag a chip to a new position** and have the order persist.
- **Feasibility (verified)**: Fully supported already — no schema change. `JobDdlOptionsDto` carries each category as an ordered `string[]`; GET/PUT preserve order; dirty-detection already triggers the Save bar on an order change. `@angular/cdk` drag-drop (`cdkDrag`/`cdkDropList`) is already used in widget-editor, profile-editor, options-panel, and schedule build-order — proven in-repo pattern.
- **For Todd — implementation sketch**: add `DragDropModule`; wrap `.chip-list` in `cdkDropList` with `(cdkDropListDropped)`; each `.chip` → `cdkDrag`; on drop clone the array, `moveItemInArray`, `this.options.set(...)` — the existing PUT persists. Add grab-cursor cue + drag preview/placeholder to match profile-editor. **Optional pairing**: a per-category "Alphabetize" one-click button (drag still needed for custom orders like XS/S/M/L/XL).
- **Severity**: UX / Feature
- **Status**: Open (Ann, 2026-07-26)

### AM-008: [Configure / Job Settings → General] Sport dropdown needs the same whitelist + title-case cleanup as LADT
- **Topic**: Configure Menus → Job Settings → General (SuperUser section) — Sport dropdown
- **Source**: Carried forward from ConfigureMenus punchlist PL-034 (consolidation sweep, 2026-07-26)
- **Request (Ann)**: The General tab's Sport dropdown pulls from a different code path than LADT and shows the **full unfiltered `Sports` table** (stale/irrelevant entries, no title-casing). It should show the same clean **12-sport whitelist** (title-cased, sorted) that LADT already uses.
- **Two code paths surface Sports today**:
  - **LADT** — `LadtService.GetSportsAsync` ([LadtService.cs:200-225](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/LadtService.cs#L200)) filters to the whitelist (lacrosse, soccer, football, hockey, field hockey, basketball, baseball, softball, volleyball, wrestling, rugby, cheerleading) + title-cases + sorts.
  - **Job Config General** — `JobConfigService.BuildReferenceDataAsync` ([JobConfigService.cs:351](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobConfigService.cs#L351)) calls `_repo.GetSportsAsync(ct)` with no filter or casing.
- **Plan (agreed with Ann) — Option B**: extract the LADT whitelist + title-case + sort into **one shared helper** (`SportListHelper` / `ISportOptionProvider`); route both `LadtService.GetSportsAsync` and `JobConfigService.GetReferenceDataAsync` through it; each service maps to its own DTO shape. Backend change → needs API deploy. **Do not implement until Todd signs off.**
- **Scope bonus**: audit other Sport-pulling paths (job-clone wizard, customer-setup, reports) and route them through the same helper so no surface drifts back to the raw table.
- **Severity**: UX
- **Status**: Open — awaiting Todd go/no-go (Ann, 2026-07-26)

### AM-009: [Configure / Job Settings → Payment] Payment tab — Refund Policy relocation + Balance Due % / Mail-in Warning cleanup
- **Topic**: Configure Menus → Job Settings → Payment tab
- **Source**: Carried forward from ConfigureMenus punchlist PL-044 + PL-045 (consolidation sweep, 2026-07-26)
- **Two Payment-tab items:**
  1. **Move Refund Policy onto the Payment tab, job-type-aware (PL-044)** — refund policies live on two separate tabs today (`PlayerRegRefundPolicy` on Player, `AdultRegRefundPolicy` on Coaches). Consolidate both into a "Refund Policy" fieldset under **Payment**, shown by job type: Player sites → Player policy; Tournament (JobTypeId=2) → Club Rep/Team policy; League (3) → both.
     - **Open decisions**: (a) which JobTypeIds count as "player sites"; (b) confirm the UI label "Club Rep / Team Refund Policy" over "Adult" (DB column `AdultRegRefundPolicy` unchanged); (c) does the **Coaches tab survive** after losing this field (it's a main section today, tab reads thin afterward).
     - **Consolidation**: most clubs have one refund policy for everything — recommend one editor. **A** first (Payment-tab editor writes **both** `playerRegRefundPolicy` + `adultRegRefundPolicy`, zero-risk UX win), then **B** cleanup (drop `adultRegRefundPolicy`, single source of truth). Store Refund Policy on Mobile & Store stays out of scope.
  2. **Clarify Balance Due % and Mail-in Payment Warning (PL-045)** — underspecified; at least one bug:
     - **🔴 Gap #1 (Bug)**: the **Mail-in Payment Warning renders unconditionally** on the payment step ([payment-step.component.ts:174-177](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/steps/payment-step.component.ts#L174)) — it shows even when the job is **CC Only**. Gate it: `@if (mailinPaymentWarning() && allowsCheck())` where `allowsCheck` is `paymentMethodsAllowedCode ∈ {2 CC-or-Check, 3 Check-Only}`.
     - **Gap #2 (Verify)**: confirm whether Balance Due % / Mail-in Warning flow into **confirmation emails + on-screen confirmation** — no grep hit in email templates; check `RegistrationConfirmation` / `TeamRegConfirmation` + Registration Complete pages.
     - **Gap #3 (UX)**: no Director help text on either field. Add `.field-help` — Balance Due %: *"Percentage of the total team fee collected as a deposit at registration. The remaining balance is invoiced/collected later. Only applies when 'Teams Full Payment Required' is unchecked."*; Mail-in Payment Warning: *"Message shown to parents/teams on the payment screen when they choose to pay by check. Only displayed if 'Allowed Methods' includes check."* Also gray out Balance Due % when `bTeamsFullPaymentRequired = true` (its effective no-op state). Note: Balance Due % is stored as a **string** (`Balancedueaspercent`) — odd type worth questioning.
  3. **Add a Save button to the upper right (PL-071)** — the Payment tab's Save action is only at the **bottom right**, so after editing it's easy to miss. Add a Save at the **upper right** too. Surfaced on Payment but **almost certainly applies to the other Configure tabs as well** — treat as a cross-tab decision. **Design call for Todd**: duplicate Save top **and** bottom, or a single **sticky** Save that stays visible while scrolling; and confirm the scope is all Job Configuration tabs.
     - **Broader review (added per Todd, 2026-07-26)**: **audit ALL Director screens**, not just the Configure/Job-Settings tabs, to decide where a Save belongs in the **upper right in addition to the bottom**. Walk every Director-facing screen with a Save/persist action, note which are long enough to scroll the bottom Save out of view, and land **one consistent Save-placement pattern** across them (top+bottom vs sticky). Goal: a Director never edits a long form and loses track of where Save is.
- **Severity**: UX + Bug (Gap #1 mail-in warning leaks onto CC-only jobs)
- **Status**: Open (Ann, 2026-07-26)

### AM-010: [Configure / Job Settings → Communications] "Turn off Player & Staff Confirmations" — 🔴 label promises "for tournaments" but the setting is NOT gated
- **Topic**: Configure Menus → Job Settings → Communications tab
- **Source**: Carried forward from ConfigureMenus punchlist PL-048 (consolidation sweep, 2026-07-26)
- **Label rename — already done**: the checkbox now reads "TURN OFF Player & Staff Confirmations (CC & BCC) for tournaments" (backing field `bDisallowCcplayerConfirmations`, no DB/DTO/service change).
- **🔴 NOT-GATED ISSUE (the open part)**: the label now claims **"for tournaments,"** but nothing actually restricts the setting to tournament jobs — the checkbox renders on **every** job type and the backend CC/BCC suppression is **not** verified to be job-type-scoped. So the label currently over-promises: a Director on a player/league site sees "for tournaments" on a control that (a) still shows and (b) may still act. **Two things to resolve:**
  1. **Verify backend behavior** — does the CC/BCC suppression actually gate on job type, or does it apply to any job with the flag set? Confirm before changing UI.
  2. **Make label and behavior agree** — if it's tournament-only, wrap the checkbox in `@if (jobTypeId === JobTypeTournament)` so it doesn't render on non-tournament sites; if it applies to any job, **drop "for tournaments"** from the label (or tighten the backend to match).
- **Severity**: UX + Bug (label/behavior mismatch)
- **Status**: Open (Ann, 2026-07-26)

### AM-011: [Configure / Job Settings → Coaches + Club Reps/Teams] Club Rep confirmation text shares the Coaches (Adult-flow) template
- **Topic**: Configure Menus → Job Settings → Coaches tab + Club Reps/Teams tab; downstream team-registration confirmation
- **Source**: Carried forward from ConfigureMenus punchlist PL-062 (consolidation sweep, 2026-07-26)
- **Finding (verified)**: Team Registration confirmation reads `AdultRegConfirmationEmail` / `AdultRegConfirmationOnScreen` — **the same templates edited on the Coaches tab** (`TeamRegistrationService.SendClubRepConfirmationEmailAsync`). TSIC's "Adult" flow lumps Coaches, Staff, AND Club Reps together, so whatever the Director writes reaches all three roles — e.g. "Welcome, Coach!" lands on a Club Rep registering teams. Only one set of Adult confirmation fields exists on the Jobs entity; not hardcoded, not duplicated — shared.
- **Options for Todd**:
  - **A. Status quo (keep shared)** — one Adult template for all three roles; relies on neutral wording. No schema change.
  - **B. Separate Club Rep templates** — new `clubRepRegConfirmationEmail` / `clubRepRegConfirmationOnScreen` fields + a Confirmation Text card on the Club Reps/Teams tab; `TeamRegistrationService` switches to them. Cleanest per-role UX; schema additions + clone-path update.
  - **C. Single template + `!ROLE` token** — keep one Adult template; add a `!ROLE` substitution resolving to "Coach"/"Staff"/"Club Rep" at send time. Director writes once, correct wording per recipient. Fits the existing `!`-token convention.
  - **Recommendation**: **C** — simplest, removes the shared-text problem; **B** if Club Reps need distinctly different content (e.g. team-management instructions).
- **Decision points**: (1) is the shared Adult-flow behavior intentional or a Legacy quirk to fix? (2) if separating, full per-role text (B) or token substitution (C)?
- **Severity**: Question
- **Status**: Open — Todd discussion (Ann, 2026-07-26)

### AM-012: [Configure / Job Settings → Player + Coaches] Normalize oversized text in migrated Legacy RTE content
- **Topic**: Configure Menus → Job Settings → Player tab + Coaches tab; downstream registration flows
- **Source**: Carried forward from ConfigureMenus punchlist PL-057 (consolidation sweep, 2026-07-26)
- **What it is**: Legacy migration baked `<h1>`/`<h2>`/`font-size` styles into RTE content (Release of Liability, Code of Conduct, etc.), so headings render dramatically larger than body text in the registration flows. **This is legacy-data only** — the RTE toolbar already restricts to Bold/Italic/Underline/FontColor/BG/Lists/Link (no heading dropdown, no font-size picker), so **future content stays clean automatically**.
- **Affected** (10 RTE fields per job × all jobs):
  - **Player tab**: `playerRegConfirmationEmail`, `playerRegConfirmationOnScreen`, `playerRegRefundPolicy`, `playerRegReleaseOfLiability`, `playerRegCodeOfConduct`, `playerRegCovid19Waiver`
  - **Coaches tab**: `adultRegConfirmationEmail`, `adultRegConfirmationOnScreen`, `adultRegRefundPolicy`, `adultRegReleaseOfLiability`, `adultRegCodeOfConduct`
- **Fix options**:
  - **A. One-time cleanup** — strip `<h1>`–`<h6>` tags + inline `font-size:` styles across all affected rows; replace a former heading with `<strong>` (preserves emphasis) or `<p>` (full normalization). Best as a one-time **C# migration** (load row → `Regex.Replace` → write back) rather than raw SQL pattern-matching; keep it as an auditable/repeatable script in `scripts/`.
  - **B. Display-time sanitize pipe** — strip oversized formatting on render in the player/adult registration flows; source preserved.
  - **C. Both** — A for clean editor source, B as a render safety net.
  - **Recommendation**: **A** — the toolbar already prevents new bad content, so a one-time data fix is sufficient.
- **Decision points for Todd**: (1) replacement strategy `<strong>` vs `<p>`; (2) scope = just these 10 fields, or also bulletins / banners / other RTE content; (3) OK to rewrite all jobs in one pass.
- **Severity**: UX / Data cleanup
- **Status**: Open (Ann, 2026-07-26)

### AM-013: [Configure / Job Settings → Teams → Player + Coaches] Relocate Roster Visibility checkboxes per role + add explanatory copy
- **Topic**: Configure Menus → Job Settings → Teams tab (current) → Player + Coaches tabs (proposed)
- **Source**: Carried forward from ConfigureMenus punchlist PL-058 (consolidation sweep, 2026-07-26)
- **What it is**: The Teams tab has a "Roster Visibility" section with two checkboxes labeled bare "Adult" / "Player" and no help text. Move each role-gating toggle to its role's tab and give it self-explanatory copy.
- **Two-part change**:
  1. **Move per role** — `bAllowRosterViewPlayer` → Player tab; `bAllowRosterViewAdult` → Coaches tab. **DB columns stay put** (`Jobs.BAllowRosterViewPlayer` / `BAllowRosterViewAdult`); only DTO/service mapping shifts: move `bAllowRosterViewPlayer` from `JobConfigTeamsDto` → `JobConfigPlayerDto` and `bAllowRosterViewAdult` → `JobConfigCoachesDto`; `UpdatePlayerAsync` / `UpdateCoachesAsync` set the respective column, strip from `UpdateTeamsAsync`. Runtime gate (`MyRosterService`) unaffected — reads the same columns.
  2. **Clarify language**:
     - Player tab: **"Allow players to view their team roster"** — *"When enabled, registered players can see their teammates' names on their team page."*
     - Coaches tab: **"Allow coaches & staff to view their team roster"** — *"When enabled, coaches and club reps can see all rostered players on their team page."*
- **Decision points for Todd**: (1) confirm the per-role move vs keeping a "team visibility" cluster on Teams; (2) confirm labels match TSIC terminology; (3) audit whether other Teams-tab fields belong on a role-specific tab.
- **Severity**: UX
- **Status**: Open — Todd discussion (Ann, 2026-07-26)

### AM-014: [Configure / Job Settings → Mobile & Store] Break Mobile Features into TSIC-Events and TSIC-Teams subsections
- **Topic**: Configure Menus → Job Settings → Mobile & Store tab
- **Source**: Carried forward from ConfigureMenus punchlist PL-067 (consolidation sweep, 2026-07-26)
- **What it is**: All seven Director-visible Mobile fields render in **one undifferentiated block**, so it's unclear which toggle affects which app. Split into two subsections, each with its own Enabled master toggle.
- **Proposed grouping** (Todd to confirm against actual app consumption — which flag the Events app vs Teams app actually reads):
  | Subsection | Master toggle | Sub-toggles |
  |---|---|---|
  | **TSIC-Events** | `tsicEventsEnabled` (inverse of `bSuspendPublic`) | `mobileScoreHoursPastGameEligible` (game scoring is events) |
  | **TSIC-Teams** | `bEnableTsicteams` | `bEnableMobileRsvp` (RSVP = team event), `bEnableMobileTeamChat` (chat = team) |
  | **Cross-cutting** | — | `bAllowMobileLogin`, `bAllowMobileRegn` (gate functionality across both) |
- **UI shape**: two distinct cards/fieldsets, each master toggle at top; sub-toggles disable when master is off (parent-child pattern, like `bRegistrationAllowPlayer` → `bPlayerRegRequiresToken` on the Player tab). Cross-cutting toggles in their own card below.
- **Decision points for Todd**: (1) confirm field-to-subsection mapping against actual app consumption; (2) cross-cutting toggles in their own "Mobile App Access" subsection or duplicated into both cards; (3) master-OFF behavior — UI-disable sub-toggles only, or also clear the sub-flags in DB on save; (4) SuperUser-only Store fields stay in their existing dedicated section (no change).
- **Cross-ref**: if PL-066's "Push Directors" feature is ever restored (currently marked Fixed/vestigial), it would land on Mobile and affect subsection placement.
- **Severity**: UX
- **Status**: Open — Todd discussion (Ann, 2026-07-26)

### AM-015: [Configure / Job Clone] Job Clone wizard — consolidated open items (Todd + Ann to work once all else is reviewed)
- **Topic**: Configure Menus → Job Clone wizard (SuperUser-only)
- **Source**: Carried forward from JobClone punchlist PL-001 / PL-002 / PL-003 / PL-004 (consolidation sweep, 2026-07-26)
- **Handling**: Deferred for now — **Todd and Ann will work Job Clone together once the rest of the review is done.** All four items live here as one cluster so nothing is lost.
- **Four open items:**
  1. **🔴 Step 7 "Create job" button appears dead (PL-004, Bug/major blocker)** — the button is `[disabled]` until an easy-to-miss affirmation checkbox ("I've reviewed the above and want to create the job") is ticked. A disabled HTML button fires no click event, so it silently does nothing with no feedback. **Verify** the checkbox tick actually enables it (if not, it's a real OnPush/zoneless CD bug — convert `affirmationChecked` to a signal). **UX fix** regardless — rec **D**: replace the pre-blocking checkbox with a confirm-modal fired from the button (single click → "Are you sure?" → confirm).
  2. **Step 3 Admin/User Expiry default to today+1yr instead of source+1yr (PL-002, Bug)** — both expiry fields default to `new Date()` + 1yr, ignoring the source job's existing expiry, so a clone lands ~6 months off the seasonal cadence. **Two-part fix**: (a) backend — extend `JobCloneSourceDto` + source projection to carry `ExpiryAdmin` / `ExpiryUsers`; (b) frontend — default to `(source.expiry ?? today) + 1yr`, falling back to today+1 only when source expiry is null (clone-flavor load only).
  3. **Consolidate wizard 7 steps → 2 steps with 4 cards in Step 1 (PL-003, UX/Feature)** — proposed shape: Step 1 = Identity / Dates / LADT scope / Fees cards (parallel), Step 2 = Review & submit. Redistributes the eliminated Step 6 fields (reg-from-email → Identity, parallax toggle + grad-year advance → LADT scope; drop the "DOB windows" half per PL-008). **Decisions**: confirm the 4-card shape, field homes, DOB-windows drop, per-step backend validations (esp. moving the `jobIdentityExists` uniqueness check to the Step 1→2 transition), and stack vs 2×2 layout.
  4. **Step 2 Reset and Back buttons look redundant (PL-001, UX)** — on Step 2 both return to Step 1, though they differ (Reset = destructive wipe, preserves flavor; Back = non-destructive step decrement). Rec **B**: rename Reset → "Start Over" with a confirm dialog to make its destructive nature explicit (optionally also hide it on Step 2 where the destination matches Back).
- **Severity**: Bug (PL-004, PL-002) + UX (PL-003, PL-001)
- **Status**: Deferred — Todd + Ann to work Job Clone after the rest of the review (Ann, 2026-07-26)
