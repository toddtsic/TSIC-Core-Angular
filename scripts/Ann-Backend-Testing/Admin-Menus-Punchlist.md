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

### AM-004: [Configure / Administrators] "Add Administrator" accepts ANY account — including shared family logins — as a job admin
- **Topic**: Configure Menus → Administrators → Add Administrator (username search)
- **Source**: Carried forward from ConfigureMenus punchlist PL-006 (consolidation sweep, 2026-07-26)
- **Severity correction (Todd + Claude, 2026-07-27)**: the original 🔴 "cross-customer privilege escalation" framing was **overstated** — the entire `AdministratorsController` is `[Authorize(Policy = "SuperUserOnly")]` and the route is `roles: [Roles.Superuser]`, so **only SuperUsers can reach any of it**; a Director at Customer A cannot add anyone anywhere. SuperUser is cross-customer by design.
- **The REAL problem (Todd, 2026-07-27)**: the search surfaced **every account on the platform** (mostly shared-credential **family logins**) indistinguishably, and `AddAdministratorAsync` accepted **any resolvable username with zero eligibility checks** — one mis-pick and a household's shared family password carries live Director access to the job's PII/medical/financial data. No warning at search, add, or review. The tool had no concept of admin-account hygiene.
- **Agreed design (Todd, 2026-07-27) — implemented**:
  1. **Eligibility whitelist, enforced server-side in both search and add**: only two account shapes are admin candidates —
     - **Admin-only** accounts (every registration carries an admin role) → pinned with a new Director-category registration; duplicate-on-job rejected.
     - **Unassigned Adult–only** accounts with ≥1 registration on this customer (the pending-coach funnel) → accepting **converts their pending registration in place** (role → chosen admin role, category → Director, retargeted to current job, fees zeroed), which also removes them from the coach-approval queue atomically. Guards: reject if `PaidTotal ≠ 0`, reject if no pending reg with this customer.
     - Any family/player footprint (incl. being any registration's `FamilyUserId` credential holder) → **never surfaced, rejected on add** with instructive message.
  2. **New-admin funnel**: brand-new person → registers as coach/staff adult on the job's site (personal account) → appears badged **Pending adult** → accepted here. No shared credentials by construction.
  3. **UI**: per-row **Admin account / Pending adult** badges; eligibility hint under the search box; convert explanation when a Pending adult is selected; instructive empty-state.
  4. **Help**: `helpKey: 'administrators'` + authored `help/administrators/overview.html` + `faq.html` documenting the funnel and the family-login refusal.
- **Code**: `UserRepository.SearchAdminCandidatesAsync` (replaces unscoped `SearchAsync`), `AdministratorService.AddAdministratorAsync` eligibility wall + convert path, `IAdministratorRepository.GetRegistrationsByUserIdAsync`/`IsFamilyCredentialHolderAsync`, `UserSearchResultDto.AccountType`, admin-form-modal badges/hints.
- **Eligibility predicate — exact semantics (Todd + Claude, 2026-07-27)**: quantified over the account's registrations **platform-wide** (any job, any customer), never job- or customer-scoped — roles are per-registration but **credentials are per-account**, so a family password shared at Customer B unlocks whatever we grant at Customer A. Per shape, the predicate is two-part to kill the vacuous-truth hole (`All()` over an empty set is true): **ANY(reg exists) AND NOT ANY(reg outside the shape)** —
  - Pending adult: `ANY(reg is UnassignedAdult on this customer)` **AND** `NOT ANY(reg is any other role, anywhere)`. Zero-reg accounts are refused explicitly, not passed vacuously.
  - Admin: `ANY(reg exists)` **AND** `NOT ANY(reg outside the 7 admin roles, anywhere)`.
- **Why an approved/active coach can never surface (verified 2026-07-27)**: coach approval **mints a separate `RoleId = Staff` registration per granted team** (`RosterSwapperService.ExecuteTransferAsync` FLOW 2), leaving the UA row as an anchor. Any approved coach therefore carries ≥1 Staff reg → breaks both ALLs → excluded from search and refused on add. The only UA-only accounts in existence are genuinely-pending, never-granted adults — exactly the funnel population. (A *pending* coach on another job of this customer IS offered — deliberate; accepting consumes their pending request, surfaced by the badge + convert warning at pick time.)
- **REFINEMENT — per-role lanes (Todd, 2026-07-27, second pass)**: Todd's E2E caught that the tool grants **six different admin types** but the first-pass wall used one "any admin role" bucket — an account with Director history could be handed Store Admin, etc. **Decided: no cross-type grants ("too confusing from a security POV")**. Eligibility for granting role X = accounts whose **every registration anywhere is within X's lane** ∪ UA-only-on-this-customer. Lanes: **{Director, SuperDirector} shared** (same person at two trust levels; mixed D/SD accounts exist in real data, e.g. aim_kl, and the Edit modal already flips between them — confirmed by Todd); **Ref Assignor, Store Admin, STPAdmin, ApiAuthorized each strictly their own lane**. Explicitly decided: Ref Assignor does NOT admit referee-footprint accounts — a referee becoming an assignor gets a fresh account through the UA funnel. UI flow flipped: **Role is picked first, then search** (search request carries the role; results/hints/empty-state are role-aware). Implemented in `GetRoleLane` (AdministratorService), `SearchAdminCandidatesAsync(laneRoleIds)`, modal role-first rework; help pages updated to the lane model.
- **Severity**: Security hardening (latent cross-tenant path if screen ever opens to Directors) + UX
- **Status**: IN PROGRESS — lane model coded 2026-07-27, awaiting Todd E2E

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
- **DISPOSITION (Todd, 2026-07-27): Won't Fix — the item's premise doesn't survive scrutiny.**
  - **The two columns are independent dimensions, both truthful.** Status = the director's enable **switch** (on/off). Expiry = the **calendar window**. "Expired + Active" is not an oxymoron — it's a real state: *switch on, out of window* (extend the end date and the code resumes without touching the toggle). The proposed computed Status would have **destroyed information** by hiding the switch position exactly when a director is extending a season's code.
  - **Backend check: verified SAFE.** The redemption path `JobDiscountCodeRepository.GetActiveCodeAsync` requires `Active && CodeStartDate <= now <= CodeEndDate` — an expired-but-toggled-on code **cannot be redeemed**. No money-path exposure; display-layer question only.
  - **No reader is actually misled**: the Expiry chip already shows **"Expired" in red** on the same row — the salient fact is on screen, prominent and correct.
  - **Lock icon (part 1) also declined**: the Usage count sits in the same row, used-codes-can't-be-deleted is the guessable audit-trail rule, and wanting to delete a used code is rare. Not worth churn this close to go-live.
- **Severity**: UX + Bug (stale/incorrect Active status) — downgraded on review: no bug exists
- **Status**: Won't Fix (Todd, 2026-07-27) — semantics correct and verified; no code change

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

### AM-016: [Configure / Widget Editor] The Widget Editor's "public" settings do nothing
- **Topic**: Configure Menus → Widget Editor (SuperUser-only)
- **Source**: Brought forward from ChelseaReview **CR-117** (2026-07-27)
- **⚠️ Note for Todd**: carried forward **as DEFERRED** — CR-117 was already marked "DEFERRED (per Todd, 2026-07-16) — revisit later"; Ann is bringing it into the AM queue so it's tracked for action, not lost, but it stays deferred until Todd picks it up.
- **What's broken (Bug)**: The Widget Editor has a **Public** panel with on/off switches for the banner, bulletins, event-contact, and job-pulse widgets. **Nothing reads them.** The public landing page hard-codes `<app-client-banner>` + `<app-bulletins>` (`job-landing.component.html:42,72`), so toggling a public widget off changes nothing. Two of the widgets (**Event Contact**, **Job Pulse**) are **never rendered anywhere at all**.
- **Why it matters**: A SuperUser configures these switches and sees no effect whatsoever. Event Contact in particular would be genuinely useful on a public page and simply isn't wired up.
- *Dev evidence*: landing hard-codes the widgets (`job-landing.component.html:42,72`); dashboard reads only the dashboard workspace (`widget-dashboard.component.ts:77`); the two widgets are referenced only in the registry, never rendered.
- **Severity**: Bug (config with no effect)
- **Status**: Deferred (brought forward from ChelseaReview CR-117; per Todd 2026-07-16)

### AM-017: [Configure / Theme] Theme editor only saves to the current browser — nobody else sees the change
- **Topic**: Configure Menus → Theme editor (SuperUser-only)
- **Source**: Brought forward from ChelseaReview **CR-125** (2026-07-27)
- **⚠️ Note for Todd**: carried forward **as DEFERRED** — CR-125 was already marked "DEFERRED (per Todd, 2026-07-16) — revisit later"; Ann is bringing it into the AM queue so it's tracked, but it stays deferred until Todd picks it up.
- **What's broken (Bug)**: Configure → Theme's Save button is literally labelled **"Save (LocalStorage)"** — the colours are stored **in that one browser only**. Nothing persists to the server, so no visitor, family, or other admin ever sees the change. On top of that, **3 of its 5 theme targets emit styling that is never applied** to anything.
- **Extra (security-adjacent)**: `/brand-preview` — an internal design showcase — is **publicly reachable on every event's URL with no login required** (`app.routes.ts:118-121`, no auth guard). Harmless content, but it shouldn't be on a client's public site.
- **Why it matters**: Per-event colours look configurable but aren't — anyone who "brands" an event this way believes they've changed it and is the only person who can see it.
- *Dev evidence*: localStorage-only persistence (`theme-editor.component.ts:78, 274-288`; `theme-overrides.service.ts:42-68`); `/brand-preview` route has no auth guard (`app.routes.ts:118-121`).
- **Severity**: Bug (config with no effect) + public-route exposure
- **Status**: Deferred (brought forward from ChelseaReview CR-125; per Todd 2026-07-16)

### AM-018: [Configure / Communications] Directors/office no longer get a copy of every player confirmation email
- **Topic**: Configure Menus → Communications (player confirmation email recipients)
- **Source**: Brought forward from ChelseaReview **CR-012** (2026-07-27)
- **Type**: Workflow-change — **needs a decision**
- **What's new**: In the old system the player confirmation email copied the director/office via the job's CC/BCC email fields. The new system sends the confirmation **only to the family and player** — no CC or BCC.
- **Why it matters**: Directors/offices who relied on getting a copy of every registration will quietly stop receiving them — expect "we're not getting registration copies anymore." Decide whether to bring the director copy back (and whether it should be a per-job toggle).
- **Interacts with**: **AM-010** (the "Turn off Player & Staff Confirmations (for tournaments)" checkbox) and the **team-vs-player asymmetry** — team/club-rep confirmations still CC/BCC the office via the Comms-tab lists; player confirmations ignore those lists. So any "bring the copy back" decision should reconcile both paths.
- *Dev evidence*: recipients are family+player only (`PaymentService.cs:2453-2468`), no CC/BCC wiring on the player confirmation path.
- **Severity**: Question / workflow decision (Legacy-parity gap)
- **Status**: Open — Todd decision (Ann, 2026-07-27)

### AM-019: [Configure / LADT] Restore the "0 = unlimited" warning when a Director sets a 0 Max Roster or Max Teams
- **Topic**: Configure Menus → LADT → Team Details (Max Roster) + Age Group (Max Teams)
- **Source**: Brought forward from ChelseaReview **CR-047** (Ann, 2026-07-27)
- **Request (Ann)**: **Please add a warning for the Director when he sets a 0 Max Roster or Max Teams.** In the old system, setting Max Roster to 0 popped *"a roster max of 0 means UNLIMITED ROSTER SIZE."* The new system shows no such warning — 0 is treated as unlimited **silently**, a quiet trap for a Director who enters (or leaves) 0.
- **Confirmed in code (Ann + Claude, 2026-07-27) — 0 does mean unlimited**:
  - **Max Roster (per team)** — a team is "full" only when `current >= MaxCount && MaxCount > 0`, so **MaxCount 0 → never full → unlimited** ([TeamLookupService.cs:67](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamLookupService.cs#L67)).
  - **Max Teams (per age-group)** — fullness gated on `ageGroup.MaxTeams > 0`; code comment: *"MaxTeams<=0 means uncapped → never fills"* ([TeamRegistrationService.cs:865](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamRegistrationService.cs#L865)).
  - *(Max Teams per Club is fully retired per PL-041 — always unlimited now; not in scope here.)*
- **For Todd — the change**: add an inline warning/help note on the **Max Roster** input (Team Details) and the **Max Teams** input (Age Group) that fires when the value is 0, e.g. *"0 = unlimited — no cap will be applied."* Restores the old system's guardrail so a Director knows a 0 means uncapped, not misconfigured.
- **Severity**: UX (silent "0 = unlimited" trap — worth restoring the warning)
- **Status**: Open (Ann, 2026-07-27)

### AM-020: [Auth / Login] Password reset works by email only now — bring back the username path
- **Topic**: Login / Forgot Password (Client support + SuperUser/Admin)
- **Source**: Brought forward from ChelseaReview **CR-067** (Ann, 2026-07-27)
- **Type**: Workflow-change — needs a decision
- **What's new**: The old "forgot password" looked you up by **username first, then email**, and also matched a parent's family-account email. The new one takes an **email address only** — no username option, and doesn't check family emails. It also always replies "if an account with that email exists, a link has been sent" (won't say whether the account was found).
- **Why it matters**: A user who remembers only their **username** — common for adult and admin accounts — can't reset their password from the form anymore, and a parent whose login email differs from the one on file may not be found. A real "I can't reset my password" support case.
- **Request (Ann, 2026-07-27)**: **The username option is very useful here given the rationale presented** — bring back the username path (and consider re-matching family/parent emails) so adult/admin users who only remember their username can reset their password.
- *Dev evidence*: CR-067 — new reset takes email only, no username lookup, no family-email match.
- **Severity**: UX / Legacy-parity (support-impacting)
- **Status**: Open — Todd decision (Ann, 2026-07-27)

### AM-021: [Coach Approval] Approving or denying a coach doesn't notify the coach
- **Topic**: Roster Swapper → Coach Approval Queue (Client support + SuperUser/Admin)
- **Source**: Brought forward from ChelseaReview **CR-089** (Ann, 2026-07-27)
- **Type**: Workflow-change — needs a decision
- **What's new**: Neither **approving** a coach onto teams nor **denying** them sends the coach any notification. They find out by logging in — if they think to. (The old system didn't notify either, but it also had no approval step.)
- **Why it matters**: Combined with the previously-missing registration confirmation (CR-084, now resolved), a coach registers, is told an email is coming, gets nothing, then is approved/denied in silence.
- **Request (Ann, 2026-07-27)**: **Is it possible to send an email notification automatically upon approval under Coach Approval?** (And likely on denial too.) Add an automatic email to the coach when a Director approves (and/or denies) them in the Coach Approval queue.
- *Dev evidence*: `RosterSwapperService` has no email service injected; neither approve nor deny sends mail ([RosterSwapperService.cs:20-44, 461-483](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/RosterSwapperService.cs#L461)).
- **Severity**: UX / workflow (silent approve/deny)
- **Status**: Open — Todd decision (Ann, 2026-07-27)

### AM-022: [Roster Visibility / Privacy] 🔒 A logged-in parent can download every family's contacts + every child's DOB as a PDF
- **Topic**: Roster view ("Allow Roster View — Player") → My Roster PDF export (privacy)
- **Source**: Brought forward from ChelseaReview **CR-094** (Ann, 2026-07-27)
- **Type**: Workflow-change — **privacy decision**
- **🔒 What's new**: When "Allow Roster View — Player" is on, a logged-in player/parent sees the team roster — including each person's email, phone, **date of birth**, and **both parents' names, emails and phone numbers** (true in the old system too). **What changed**: the same parent can now **download the whole roster as a PDF**, parent-contact + DOB columns included. The old system showed it on screen only; it didn't hand out a file.
- **Why it matters**: Enabling roster view for players hands every parent on the team an **offline, bulk copy** of every other family's contact details and every child's birthdate — a real privacy exposure.
- **For Todd — the decision**: e.g. **redact the contact/DOB fields for the player audience**, or make the **PDF admin-only** (the on-screen roster and the PDF currently share the same visibility gate, so redaction/gating must be applied to both, or the PDF split off to an admin-only role).
- *Dev evidence*: roster data carries DOB + Mom/Dad email/phone with **no role filter** ([MyRosterDtos.cs:34-52](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Dtos/MyRoster/MyRosterDtos.cs#L34), [RegistrationRepository.cs:2639-2699](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L2639)); PDF endpoint uses the same visibility gate as the on-screen roster ([MyRosterController.cs:36-52](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/MyRosterController.cs#L36), [MyRosterPdfService.cs:95-100](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Reporting/MyRosterPdfService.cs#L95)).
- **Severity**: 🔒 Privacy (bulk PII/DOB export to the player audience)
- **Status**: Open — Todd decision (Ann, 2026-07-27)
