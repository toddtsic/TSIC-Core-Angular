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
- **The real decision is a product/brand one, not technical**: the minimal toolbar was almost certainly a **deliberate brand-safety choice** — fewer fonts/colors/sizes means a director can't produce off-brand content. This is the exact tension **PL-043-T** (payment punchlist) already flags: *"more expressive range (tables, sectioning) without regaining the ability to break brand."* So the question for Todd is **which tools to allow back in**, and how to **constrain styles** so the added power doesn't reopen brand-break risk.
- **For Todd — the change**:
  1. **Expand the toolbar** — add the wanted tools to the `items` list and enable the needed modules (e.g. **Table**, **Image**). Reaching Legacy parity is a config change; no new dependency.
  2. **Constrain, don't just open** — pair it with **PasteCleanup** + an allowed formats/colors policy so directors gain tables/sectioning without off-brand fonts/colors (the PL-043-T guardrail).
  3. **Shared-config caution** — `JOB_CONFIG_RTE_TOOLS` is shared across **bulletins, job-config HTML fields, and the help editor**, so widening it hits all three. Decide whether to **enrich the shared config** or give **bulletins its own richer config** (leaving job-config/help minimal).
  - **Cross-ref**: PL-057 #5 (payment punchlist) asks to add an RTE to the **ARB defensive email** (today a bare textarea); it should **reuse whatever toolbar config lands here** so the editing experience is consistent.
- **RESOLUTION (Todd, 2026-07-27): DEFERRED — bulletin editor stays as-is; enrich in response to client needs over time.** The minimal 10-tool toolbar is the deliberate brand-safety default. When a client need surfaces, the agreed shape is on file: bulletins-only `BULLETIN_RTE_TOOLS` (Formats capped at H2/H3, Alignments, Outdent/Indent, CreateTable, HorizontalLine, StrikeThrough, ClearFormat, PasteCleanup; FontName/FontSize/Image/SourceCode stay out), job-config + help editors stay minimal (their toolbar is what keeps AM-012's "future content stays clean" guarantee).
- **Status**: DEFERRED (Todd, 2026-07-27) — no change now; revisit on client demand

### AM-002: [Configure / Administrators] Administrators table — match Search/Player table style and reorder columns
- **Topic**: Configure Menus → Administrators table
- **Source**: Carried forward from ConfigureMenus punchlist PL-001 (consolidation sweep, 2026-07-26)
- **Tested**: Configure → Administrators menu → the table
- **Request (Ann)**: Make the Administrators table consistent with the Search/Player menu table:
  1. Match column heading font and style to the Search/Player table.
  2. Rename the "Status" column to "Active" and move it immediately after Name, showing "Yes" when active.
  3. Column order should be: **Name, Active, Role, Username, Registered**.
  4. Compress the table — rows and columns are too widely spaced; tighten overall (folded in from ConfigureMenus PL-004).
- **Review (Claude, 2026-07-27)**: items 1 + 4 already satisfied on current master — the grid carries the same `tsic-grid-tight` density class as the Search grids (simple-view sweep), and AM-003 added the star-in-Name treatment + MM/dd/yyyy Registered. Remaining delta was only the column reorder + Status→"Active/Yes" rename.
- **Severity**: UX
- **Status**: WON'T DO (Todd, 2026-07-27) — current table is fine; column reorder/rename declined.

### AM-003: [Configure / Administrators] Primary-contact star — reposition, and carry it forward on clone + legacy migration
- **Topic**: Configure Menus → Administrators table (primary-contact star)
- **Source**: Carried forward from ConfigureMenus punchlist PL-005 (sub-points 1, 3, 5) (consolidation sweep, 2026-07-26)
- **Tested**: Configure → Administrators → the star icons that set a job's primary contact
- **Request (Ann)** — three related fixes to make the primary-contact star behave correctly:
  1. **Reposition the star** — move it to the **left of the Director's name** for easier scanning (today it sits in the far-right Actions column).
  2. **Carry forward on job clone** — a cloned job currently lands with **no** primary contact. `JobCloneService` copies admin registrations but never copies `Jobs.PrimaryContactRegistrationId`; it must carry the primary contact through to the clone.
  3. **Legacy migration backfill (REQUIRED)** — the migration must set `Jobs.PrimaryContactRegistrationId` from Legacy's equivalent field so the star lands on the Director who was the primary contact in Legacy, preserving customer continuity. Eyeball a handful of migrated jobs to confirm.
- **Severity**: UX / Bug (clone + migration carry-forward)
- **RESOLUTION (Todd + Claude, 2026-07-27)** — reshaped by a DB census and a design upgrade:
  - **Census**: `Jobs.PrimaryContactRegistrationId` was NULL on **all 1,057 jobs** — and legacy shares the *same physical column* (same DB), so the star had never been set by anyone in either system. Sub-item 3 (legacy backfill) is **moot: there is no source data to backfill**. The feature "worked" all along because both consumers (`TextSubstitutionRepository.GetDirectorContactAsync`, `WidgetRepository.GetEventContactAsync`) silently fall back to the earliest-registered active Director/admin.
  - **Design upgrade (Todd)**: don't just display the fallback — **persist it**. `AdministratorService.EnsurePrimaryContactAsync` now seeds/repairs the star to the earliest-registered active Director whenever the persisted value is missing or invalid (runs on list load, star toggle, status toggles, bulk status). Every job now carries a real primary contact; the star is always visible in the grid. Clicking another active Director moves it; clicking the starred row reverts to the default. Server rejects starring inactive/non-Director regs; UI disables those stars.
  - **Latent FK bug fixed en route**: deleting the starred registration would have violated the `Jobs.PrimaryContactRegistrationId` FK (unreachable while the column was NULL everywhere) — delete now clears the star first; the next load re-seeds.
  - **Sub-item 1 (reposition)**: done — star sits in a fixed-width slot left of the name (`2c94ade2`); Actions tightened 150→120. Registered column reformatted MM/dd/yyyy.
  - **Sub-item 2 (clone carry-forward)**: covered by the heal — a cloned job self-seeds its star on first Administrators load once a Director is active (clone lands Directors inactive by design, so carrying the old star would have pointed at an inactive reg anyway).
  - Help (overview + FAQ) updated to the default-star semantics. Commits: `2c94ade2`, `32dcd41e`.
- **Status**: FIXED — Todd E2E verified 2026-07-27 (default star seeds, moves, reverts). Ann to verify on next pass.

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
- **RESOLUTION (Todd, 2026-07-27) — all three declined**: (1) the count badge is the standard page-header template used across all components — stays for consistency; (2) header emphasis unnecessary on a SuperUser-only screen; (3) right-pinned row actions are the deliberate responsive-design convention app-wide (matches every other row-action placement).
- **Severity**: UX
- **Status**: WON'T DO (Todd, 2026-07-27) — screen stays as-is

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
- **RESOLUTION (2026-07-27) — the "revert on save" was never the reorder mechanic; it was a latent shell bug.** History: CDK drag implemented → "reverted on save" → trackBy fix → same → drag withdrawn (CDK mixed-orientation blamed) → **plan B ‹ › nudge arrows** (pure array splice, no CDK) → *still* reverted → real cause found:
  - **Root cause (FAB save handler gap)**: Dropdowns was the ONLY Job Settings tab that never registered `svc.saveHandler` for the shell's floating Save FAB. The handler signal keeps the *previously visited tab's* save; the FAB still appears when Dropdowns is dirty (its `dirtyChange` marks the tab). Clicking it ran e.g. **General's** save ("General saved." toast — the smoking gun in Todd's screenshot), whose success path calls `loadConfig()` → `isLoading` flips → the shell's top-level `@if` **destroys and recreates the whole tab area**, wiping the Dropdowns component's unsaved edits. All three "reverts" were this; drag itself was likely fine.
  - **Fix**: `DdlOptionsComponent` now injects `JobConfigService` (optional — provided by the shell) and registers `saveHandler.set(() => this.save())` like every other tab. FAB on Dropdowns now performs the Dropdowns save (no `loadConfig()` teardown).
  - **Drag re-instated after the FAB fix**: with the real bug dead, CDK drag was re-applied (`cdkDropList cdkDropListOrientation="mixed"` on `.chip-list`, `cdkDrag` chips, `track val`, `moveItemInArray` on drop, body-appended `.chip.cdk-drag-preview` styled in `_component-overrides.scss`) and **persists correctly through save** — confirming drag was innocent all along. Shipped reorder UI = **drag + ‹ › nudge arrows** (arrows double as the keyboard/deterministic path). Help FAQ added ("Can I change the order of dropdown options?").
- **Severity**: UX / Feature + latent shell bug (FAB ran wrong tab's save on Dropdowns)
- **Status**: FIXED — Todd verified 2026-07-27 (arrows + FAB save "works"; drag re-test "works"). Ann next pass: reorder via drag or arrows → Save → reload → order holds.

### AM-008: [Configure / Job Settings → General] Sport dropdown needs the same whitelist + title-case cleanup as LADT
- **Topic**: Configure Menus → Job Settings → General (SuperUser section) — Sport dropdown
- **Source**: Carried forward from ConfigureMenus punchlist PL-034 (consolidation sweep, 2026-07-26)
- **Request (Ann)**: The General tab's Sport dropdown pulls from a different code path than LADT and shows the **full unfiltered `Sports` table** (stale/irrelevant entries, no title-casing). It should show the same clean **12-sport whitelist** (title-cased, sorted) that LADT already uses.
- **Two code paths surface Sports today**:
  - **LADT** — `LadtService.GetSportsAsync` ([LadtService.cs:200-225](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/LadtService.cs#L200)) filters to the whitelist (lacrosse, soccer, football, hockey, field hockey, basketball, baseball, softball, volleyball, wrestling, rugby, cheerleading) + title-cases + sorts.
  - **Job Config General** — `JobConfigService.BuildReferenceDataAsync` ([JobConfigService.cs:351](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobConfigService.cs#L351)) calls `_repo.GetSportsAsync(ct)` with no filter or casing.
- **Plan (agreed with Ann) — Option B**: extract the LADT whitelist + title-case + sort into **one shared helper** (`SportListHelper` / `ISportOptionProvider`); route both `LadtService.GetSportsAsync` and `JobConfigService.GetReferenceDataAsync` through it; each service maps to its own DTO shape. Backend change → needs API deploy. **Do not implement until Todd signs off.**
- **Scope bonus**: audit other Sport-pulling paths (job-clone wizard, customer-setup, reports) and route them through the same helper so no surface drifts back to the raw table.
- **RESOLUTION (Todd go 2026-07-27, implemented)**:
  - **Census first**: all 1,057 jobs carry a sport; **9 jobs sit OUTSIDE the 12-sport whitelist** — Track and Field (8) and multi-sport (1). A naive whitelist would have blanked those jobs' Sport dropdown (and a save could silently rewrite the sport). The LADT dropdown **already had this gap** for those 8 T&F customers.
  - **Implemented**: shared static `SportWhitelist` (Services/Shared/Utilities) = the 12 LADT sports **+ track and field + multi-sport** (14), with `Contains` + `ToTitleCase`. Both `LadtService.GetSportsAsync` (private copies deleted) and `JobConfigService.GetReferenceDataAsync` route through it; each keeps its own DTO shape. Helper doc-comment warns: trim only with a census, never by taste.
  - **Zero-risk audit**: read-path only, no DTO/DB change; Job Config dropdown binds `sportId` (name display-only); general tab is the sole consumer of `referenceData().sports`; the one FE sport-name comparison (`registration-detail-panel`, lacrosse check) lowercases and is fed by a different endpoint. Sport-pulling audit: nav-editor visibility options deliberately left raw (match-key surface, not a pick-a-sport dropdown); text-substitution joins are display of the job's own sport.
- **Severity**: UX
- **Status**: IN PROGRESS — coded 2026-07-27, awaiting API restart + Todd verify (General tab + LADT dropdowns show 14 clean sports)

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
- **RESOLUTION (Todd + Claude, 2026-07-27) — evidence-driven collapse of the item:**
  - **Gap #1 (CC-only leak): ALREADY FIXED before this review** — the warning renders inside `@if (isCheck())` (payment-step.component.ts:556-583, post-PL-052 rework); it only appears after the registrant picks the check method, which CC-only jobs never offer. Ann's line-cite was against the older file revision.
  - **Gap #2 (emails): VERIFIED — neither field reaches any email path.** By design: confirmation emails are Director-authored templates.
  - **DB census (TSICV5, 1,057 jobs, full platform history): `balancedueaspercent` valued 0 times ever; `mailinPaymentWarning` valued once — on the `tsic` home job itself (a test), never by a customer.** Nothing reads Balance Due % anywhere (deposits are per-team `Teams.Deposit`/`BalanceDue`); config round-trip and clone-copy are its only touchpoints.
  - **Action taken (UI-only, zero model/DTO risk per Todd)**: both **Balance Due %** and **Mail-in Payment Warning** HIDDEN from the Payment tab (template edit only; section renamed "Check / Mail-in", Pay To / Mail To widened). Component signals + save payload untouched — values round-trip exactly as loaded; DTOs/services/DB unchanged. Gap #3 help-text moot (fields gone).
  - **Items 1 (Refund Policy relocation) and 3 (Save placement audit): parked as preferences, not defects** — revisit on their own merits if desired.
- **Severity**: UX + Bug (Gap #1 mail-in warning leaks onto CC-only jobs) — downgraded: Gap #1 was already fixed; remaining fields were dead UI
- **Status**: IN PROGRESS — dead fields hidden 2026-07-27, awaiting Todd verify (Refund-Policy + Save-placement parked)

### AM-010: [Configure / Job Settings → Communications] "Turn off Player & Staff Confirmations" — 🔴 label promises "for tournaments" but the setting is NOT gated
- **Topic**: Configure Menus → Job Settings → Communications tab
- **Source**: Carried forward from ConfigureMenus punchlist PL-048 (consolidation sweep, 2026-07-26)
- **Label rename — already done**: the checkbox now reads "TURN OFF Player & Staff Confirmations (CC & BCC) for tournaments" (backing field `bDisallowCcplayerConfirmations`, no DB/DTO/service change).
- **🔴 NOT-GATED ISSUE (the open part)**: the label now claims **"for tournaments,"** but nothing actually restricts the setting to tournament jobs — the checkbox renders on **every** job type and the backend CC/BCC suppression is **not** verified to be job-type-scoped. So the label currently over-promises: a Director on a player/league site sees "for tournaments" on a control that (a) still shows and (b) may still act. **Two things to resolve:**
  1. **Verify backend behavior** — does the CC/BCC suppression actually gate on job type, or does it apply to any job with the flag set? Confirm before changing UI.
  2. **Make label and behavior agree** — if it's tournament-only, wrap the checkbox in `@if (jobTypeId === JobTypeTournament)` so it doesn't render on non-tournament sites; if it applies to any job, **drop "for tournaments"** from the label (or tighten the backend to match).
- **RESOLUTION (Todd + Claude, 2026-07-27):**
  1. **Backend verified — the switch is deliberately global.** Suppression lives at the single chokepoint `JobConfirmationCopies.Apply` (called by every confirmation path: player, team/club-rep, adult/staff, family resend, payment). `if (disallowCopies) return;` — no job-type check, and the doc comment states it kills CC/BCC "on every confirmation regardless of role — matching legacy." Gating to tournaments would change legacy-matched behavior; rejected.
  2. **Label already fixed on master** — currently reads "TURN OFF CC & BCC copies on all registration confirmations" (no "for tournaments"); this item's "renamed to …for tournaments" note was stale. Verified no "for tournaments" remains anywhere in the app.
  3. **Added** (per Todd's label-only decision): one `.field-help` line under the checkbox — copies stop, registrants always still get their confirmation, typical use = high-volume events.
- **Severity**: UX + Bug (label/behavior mismatch) — resolved: label was already truthful; help text added
- **Status**: IN PROGRESS — help text added 2026-07-27, awaiting Todd verify. **Deeper scope question moved to AM-031 (2026-07-27):** should this suppress **all roles** or be **player-specific and keep Club Rep copies** (tournament directors want those)? + SuperUser-only + spacing. Reconcile with AM-031.

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
- **Review note (Claude + Todd, 2026-07-27) — precise framing**: `Jobs` already carries role-specific override pairs `RefereeReg_ConfirmationEmail/OnScreen` + `RecruiterReg_ConfirmationEmail/OnScreen` (added 2026-02-24 `991d66b2`, real columns in prod schema) with `?? AdultReg` fallback dispatch (`AdultRegistrationService.GetConfirmationEmail/OnScreen`). There is **no `CoachReg_*` pair** — `AdultReg_ConfirmationEmail/OnScreen` is the base template doing triple duty: coach/staff adult registrations, club-rep team registrations (`TeamRegistrationService`), and fallback for referee/recruiter. **Coach and Club Rep are the only adult-flow roles without their own pair.** Symmetric end-state if ever revisited: add `CoachReg_*` + `ClubRepReg_*`, demote `AdultReg_*` to pure fallback for all four roles (matches the established house pattern; beats the `!ROLE` token).
- **Known label nit (not fixed)**: the Adult tab's section header reads "COACH Confirmation…" but its text also reaches staff and club reps; a header relabel is the only zero-risk touch available.
- **Severity**: Question
- **Status**: WON'T DO (Todd, 2026-07-27) — **no schema changes this close to go-live**. Shared Adult template remains the behavior; Directors should keep the wording role-neutral.

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
- **Census (2026-07-27, TSICV5)**: oversized markup (`<h1>`/`<h2>`/`font-size`) present in 324/1,057 jobs' player Release of Liability, 188 confirmation emails, 79 adult liability, 36 codes of conduct — real and widespread, all legacy-migrated content.
- **Status**: WON'T DO (Todd, 2026-07-27) — **no mass rewrite; respond to individual jobs as needed.** There is no reasonable blanket treatment; the toolbar already prevents new offenders, and a Director can re-edit any specific job's text on request. (A display-side CSS heading clamp was offered and declined along with the data rewrite.)

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
- **RESOLUTION (Todd, 2026-07-27)**: **toggles STAY on Teams — rosters are a team property**; the relocation half is declined. The real defect (bare "Adult"/"Player" labels) fixed in place, template-only: checkboxes stacked with descriptive labels ("Allow players to view their team roster" / "Allow coaches &amp; staff to view their team roster"), **no help lines — Todd ruled the labels sufficient** (drafted help copy was removed entirely after two corrections; one clause had described staff self-rostering, which no longer exists in the product). No DTO/service/payload changes; runtime consumers read the DB column and are untouched.
- **Severity**: UX
- **Status**: FIXED (labels + help in place, relocation declined) — 2026-07-27, awaiting Todd verify + Ann next pass

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
- **RESOLUTION (Todd, 2026-07-27) — superseded by role gating**: instead of subsectioning for Directors, **the entire Mobile/Store tab is now SuperUser-only** (`superUserOnly: true` on the tab def — hidden for Directors like Branding/Dropdowns; Mobile Features section additionally wrapped in the `isSuperUser()` gate with `form-section--super` styling to match its siblings). Rationale: app-level switches aren't Director decisions. Verified consumption map recorded for whenever the layout is revisited: `bSuspendPublic` + `mobileScoreHoursPastGameEligible` → TSIC-Events; `bEnableTsicteams` (master) + RSVP + TeamChat → TSIC-Teams-2025; `bAllowMobileLogin` → old TSIC-Teams v1 ChatAuth only; `bAllowMobileRegn` → TSIC-REGN JobValidator (neither Events nor Teams — Ann's "cross-cutting" pair is actually two legacy-app flags). The "TSIC-Events Enabled" master toggle (inverse of `bSuspendPublic`, with on/off tip) already existed on the tab. Subsection grouping for an SU audience: not needed.
- **Severity**: UX
- **Status**: FIXED (tab gated SuperUser-only) — Todd signed off 2026-07-27. Ann to verify on next pass.

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
- **RESOLUTION (2026-07-28) — ALREADY FIXED before this review; the dev-evidence cite was against an older file revision.** This gap was closed by **CR-061 ("CC/BCC split", shipped 2026-07-14/15)**, which created a single chokepoint — `Services/Shared/Email/JobConfirmationCopies.cs` — whose doc-comment describes exactly this drift ("only the team path applied copies at all… player confirmations copied nobody"). All four confirmation paths now apply the job's Comms-tab `RegFormCcs`/`RegFormBccs` + Reply-To:
  - Player (submit, ARB, eCheck-pending) — `PaymentService.cs:2356`
  - Family resend — `PlayerRegistrationConfirmationController.cs:155`
  - Team/club-rep — `TeamRegistrationService.cs:1694`
  - Adult/staff — `AdultRegistrationService.cs:659`
  The team-vs-player asymmetry is gone, and the "turn off CC & BCC" switch (`bDisallowCCPlayerConfirmations`) is honored uniformly — it suppresses the copies only, never the registrant's own confirmation.
- **Severity**: Question / workflow decision (Legacy-parity gap)
- **Status**: RESOLVED before review (by CR-061) — recorded 2026-07-28. Ann verify: register a player on a job with CC/BCC configured → office receives the copy (unless the job ticks "turn off CC & BCC", which disables copies everywhere by design).

### AM-019: [Configure / LADT] Restore the "0 = unlimited" warning when a Director sets a 0 Max Roster or Max Teams
- **Topic**: Configure Menus → LADT → Team Details (Max Roster) + Age Group (Max Teams)
- **Source**: Brought forward from ChelseaReview **CR-047** (Ann, 2026-07-27)
- **Request (Ann)**: **Please add a warning for the Director when he sets a 0 Max Roster or Max Teams.** In the old system, setting Max Roster to 0 popped *"a roster max of 0 means UNLIMITED ROSTER SIZE."* The new system shows no such warning — 0 is treated as unlimited **silently**, a quiet trap for a Director who enters (or leaves) 0.
- **Confirmed in code (Ann + Claude, 2026-07-27) — 0 does mean unlimited**:
  - **Max Roster (per team)** — a team is "full" only when `current >= MaxCount && MaxCount > 0`, so **MaxCount 0 → never full → unlimited** ([TeamLookupService.cs:67](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamLookupService.cs#L67)).
  - **Max Teams (per age-group)** — fullness gated on `ageGroup.MaxTeams > 0`; code comment: *"MaxTeams<=0 means uncapped → never fills"* ([TeamRegistrationService.cs:865](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamRegistrationService.cs#L865)).
  - *(Max Teams per Club is fully retired per PL-041 — always unlimited now; not in scope here.)*
- **For Todd — the change**: add an inline warning/help note on the **Max Roster** input (Team Details) and the **Max Teams** input (Age Group) that fires when the value is 0, e.g. *"0 = unlimited — no cap will be applied."* Restores the old system's guardrail so a Director knows a 0 means uncapped, not misconfigured.
- **RESOLUTION (Todd go, 2026-07-28)**: inline amber note (`bi-info-circle` + *"Max Roster 0 = unlimited — no cap will be applied."* / *"Max Teams 0 = …"*) added under both inputs — `team-detail.component.ts` (Max Roster) and `agegroup-detail.component.ts` (Max Teams). Shows when the value is 0 **or blank** (server coalesces both to uncapped). Scope note: the LADT grid's inline `maxCount`/`maxTeams` columns remain a second entry point for typing 0 — deliberately left alone (a grid cell can't host inline help; a popup on grid edit would be intrusive); the detail-panel note carries the education.
- **Severity**: UX (silent "0 = unlimited" trap — worth restoring the warning)
- **Status**: FIXED — coded 2026-07-28, awaiting Todd verify (Team Details → Max Roster 0/blank shows note; Age Group → Max Teams 0/blank shows note) + Ann next pass

### AM-020: [Auth / Login] Password reset works by email only now — bring back the username path
- **Topic**: Login / Forgot Password (Client support + SuperUser/Admin)
- **Source**: Brought forward from ChelseaReview **CR-067** (Ann, 2026-07-27)
- **Type**: Workflow-change — needs a decision
- **What's new**: The old "forgot password" looked you up by **username first, then email**, and also matched a parent's family-account email. The new one takes an **email address only** — no username option, and doesn't check family emails. It also always replies "if an account with that email exists, a link has been sent" (won't say whether the account was found).
- **Why it matters**: A user who remembers only their **username** — common for adult and admin accounts — can't reset their password from the form anymore, and a parent whose login email differs from the one on file may not be found. A real "I can't reset my password" support case.
- **Request (Ann, 2026-07-27)**: **The username option is very useful here given the rationale presented** — bring back the username path (and consider re-matching family/parent emails) so adult/admin users who only remember their username can reset their password.
- *Dev evidence*: CR-067 — new reset takes email only, no username lookup, no family-email match.
- **RESOLUTION (2026-07-28) — ALREADY FIXED by the forgot-password rework `a297911a` (2026-07-27), which restored legacy semantics and went further:**
  1. **Username path back** — single "Username or Email" field, no email validator (`forgot-password.component`); backend matches username first (exact hit wins), then email.
  2. **Family/parent emails matched** — lookup also matches `Families.MomEmail`/`DadEmail` (many family logins carry no email of their own) — the "parent whose login email differs" case.
  3. **Beyond legacy** — one email owning multiple accounts (family login + parent's own logins) gets **one reset email per account**, each naming its username, reset link keyed by `userId`. The pre-fix code *crashed* on duplicate emails ("Sequence contains more than one element").
  - **Deliberate non-restoration**: the neutral "if an account exists, a link has been sent" reply stays — standard anti-enumeration on an anonymous endpoint (legacy revealed account existence; we won't).
- **Severity**: UX / Legacy-parity (support-impacting)
- **Status**: RESOLVED before review (by `a297911a`) — recorded 2026-07-28; pending the API restart already queued for that fix. Ann verify: reset by username; reset by parent email on a family account; duplicate-email case → one email per account, each naming its username.

### AM-021: [Coach Approval] Approving or denying a coach doesn't notify the coach
- **Topic**: Roster Swapper → Coach Approval Queue (Client support + SuperUser/Admin)
- **Source**: Brought forward from ChelseaReview **CR-089** (Ann, 2026-07-27)
- **Type**: Workflow-change — needs a decision
- **What's new**: Neither **approving** a coach onto teams nor **denying** them sends the coach any notification. They find out by logging in — if they think to. (The old system didn't notify either, but it also had no approval step.)
- **Why it matters**: Combined with the previously-missing registration confirmation (CR-084, now resolved), a coach registers, is told an email is coming, gets nothing, then is approved/denied in silence.
- **Request (Ann, 2026-07-27)**: **Is it possible to send an email notification automatically upon approval under Coach Approval?** (And likely on denial too.) Add an automatic email to the coach when a Director approves (and/or denies) them in the Coach Approval queue.
- *Dev evidence*: `RosterSwapperService` has no email service injected; neither approve nor deny sends mail ([RosterSwapperService.cs:20-44, 461-483](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/RosterSwapperService.cs#L461)). *(2026-07-28 re-verify: file now lives at `Services/Admin/RosterSwapperService.cs` — `ApproveTeamRequestAsync` :471, `DenyCoachAsync` :486; gap confirmed still present, no email service injected.)*
- **Severity**: UX / workflow (silent approve/deny)
- **Status**: WON'T DO (Todd, 2026-07-28) — **by design; will not address.** No automatic email on approve or deny. (Proposed approve-only notification through the EmailService chokepoint was offered and declined.)

### AM-022: [Roster Visibility / Privacy] 🔒 A logged-in parent can download every family's contacts + every child's DOB as a PDF
- **Topic**: Roster view ("Allow Roster View — Player") → My Roster PDF export (privacy)
- **Source**: Brought forward from ChelseaReview **CR-094** (Ann, 2026-07-27)
- **Type**: Workflow-change — **privacy decision**
- **🔒 What's new**: When "Allow Roster View — Player" is on, a logged-in player/parent sees the team roster — including each person's email, phone, **date of birth**, and **both parents' names, emails and phone numbers** (true in the old system too). **What changed**: the same parent can now **download the whole roster as a PDF**, parent-contact + DOB columns included. The old system showed it on screen only; it didn't hand out a file.
- **Why it matters**: Enabling roster view for players hands every parent on the team an **offline, bulk copy** of every other family's contact details and every child's birthdate — a real privacy exposure.
- **For Todd — the decision**: e.g. **redact the contact/DOB fields for the player audience**, or make the **PDF admin-only** (the on-screen roster and the PDF currently share the same visibility gate, so redaction/gating must be applied to both, or the PDF split off to an admin-only role).
- *Dev evidence*: roster data carries DOB + Mom/Dad email/phone with **no role filter** ([MyRosterDtos.cs:34-52](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Dtos/MyRoster/MyRosterDtos.cs#L34), [RegistrationRepository.cs:2639-2699](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L2639)); PDF endpoint uses the same visibility gate as the on-screen roster ([MyRosterController.cs:36-52](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/MyRosterController.cs#L36), [MyRosterPdfService.cs:95-100](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Reporting/MyRosterPdfService.cs#L95)).
- **RESOLUTION (2026-07-28) — verified, and the claim narrows: the PDF does NOT contain DOB.** PDF columns are uniform #, player, pos, grad, player email/phone, Contact 1 (Mom), Contact 2 (Dad) (`MyRosterPdfService.BuildColumns`). DOB appears **on-screen only** — where legacy showed it too (legacy parity). The actual delta vs legacy is a convenience file of parent contacts the caller's own team can already see on screen, behind a **director opt-in** (`BAllowRosterViewPlayer`), own-team scope only, system-bucket teams always denied. Todd repro'd the PDF (2032 Blue Team) and reviewed.
- **Severity**: 🔒 Privacy (bulk parent-contact export to the player audience — DOB claim corrected)
- **Status**: WON'T DO — **as-is** (Todd, 2026-07-28). Working as designed: director opt-in, own-team-only, PDF is a subset of the on-screen card. (Staff-only-PDF option was offered and declined.)

---

## Landing Page

*Category introduced during Ann's pre-release walkthrough (2026-07-27). Public landing page (`tsic-landing` — the live one, not v3). Existing items AM-001…022 keep their numbers; new walkthrough finds are filed under category headings from AM-023 on.*

### AM-023: [Landing Page] Book-a-Demo area — remove the rarely-used phone number, and swap the Calendly calendar for an interim support-email inquiry until Chelsea is onboarded
- **Topic**: Public landing page → hero + "Ready to See It in Action?" (Book a Demo) CTA section
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Where**: `tsic-landing.component.html` — hero actions ([:60-67](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L60)); CTA "book-demo" section ([:231-253](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L231)); footer Contact ([:274-282](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L274)).
- **Two parts:**
  1. **Remove the rarely-used phone number by Book a Demo.** The page shows the same phone (`410-280-3272`) in **three** spots: a hero ghost-button next to "Book a Demo" ([:64-67](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L64)), the CTA "book-demo" section near the bottom ([:238-241](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L238)), and the footer Contact column ([:276-278](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L276)). Ann: it's "at the bottom of the screen and not used much at all." **Target: the CTA book-demo section phone ([:238-241])**; **keep the footer Contact phone.** *(Confirm with Ann whether the hero ghost-button phone [:64-67] should also go.)*
  2. **Decide re: the calendar.** The CTA section embeds a **Calendly** widget (`calendly.com/demo-teamsportsinfo/30min`, [:248-252](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/home/tsic-landing/tsic-landing.component.html#L248)). **For now** (until **Chelsea is onboarded**): replace the live calendar with a **badge/link to the support email** — and ideally a short **inquiry form** that captures **Name, contact info, sport, organization *(optional)*, Comment** — so demo requests still come in without a live scheduling calendar. **Later**: reinstate the real calendar once Chelsea is set up to field it.
- **Severity**: UX / pre-release content (unused phone + not-yet-ready calendar)
- **Status**: WON'T DO (Todd, 2026-07-28) — landing page stays as-is: all three phone placements and the live Calendly embed remain. (Interim email-only CTA and inquiry-form options were presented and declined.)

---

## Communications Menus

*Top-level Communications nav section (Bulletins, Email Log, E-Mail Troubleshooter, Push Notification). Ann's pre-release walkthrough (2026-07-27).*

### AM-024: [Communications menu] E-Mail Troubleshooter — confirm SuperUser-only visibility (not applied yet) and remove the NEW badge
- **Topic**: Communications nav section → **E-Mail Troubleshooter** menu item
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Where**: `scripts/5) Re-Set Nav System.sql:155` — `INSERT INTO #AdminManifest VALUES (N'Communications', …, N'E-Mail Troubleshooter', N'envelope-exclamation', N'tools/email-troubleshooter', 3, 1, 1, 1, NULL, N'NEW');`
- **Two parts:**
  1. **Confirm SuperUser-only visibility — and apply it.** Ann is confirming that E-Mail Troubleshooter was decided (with Chelsea) to be **SuperUser-visibility only**. **Code check (2026-07-27): it is NOT SuperUser-only today** — the manifest row sets `ForDirector=1, ForSuperDir=1, ForSuperUser=1`, so it currently shows for **all three admin roles**. If SuperUser-only is the confirmed decision, change the flags to `0, 0, 1` (the pattern Administrators uses at [:130](../..)), then re-run the nav reset.
  2. **Remove the NEW badge from the menu tree.** The row's `BadgeText` is `N'NEW'`, which renders the NEW chip in the nav. Set it to `NULL` to drop the chip.
- **For Todd**: edit the E-Mail Troubleshooter row at `5) Re-Set Nav System.sql:155` — flags `1,1,1 → 0,0,1` (pending confirmation of the SuperUser-only decision) and `BadgeText 'NEW' → NULL` — then re-run the nav reset so it lands in the DB. Mirror in the dev-restore nav script if one reseeds this.
- **RESOLUTION (Todd, 2026-07-28) — WON'T DO, both parts:**
  1. **Visibility**: Todd **purposefully exposed the tool to Directors/admins** — the "SuperUser-only" recollection does not stand. All three layers (nav flags `1,1,1`, route guard, API `AdminOnly` policy) already agree on admin access, consistent with deliberate design. No change.
  2. **NEW badge**: stays — a one-word cosmetic edit is **not enough reason to re-run the nav seeding script in prod**. (Note: the `.ps1` generates the `.sql`; any future change edits `5) Re-Set Nav System.ps1:205`, regenerates, then applies.)
- **Severity**: UX + role-visibility (menu shown to more roles than intended; stale NEW chip)
- **Status**: WON'T DO (Todd, 2026-07-28) — admin visibility is intentional; badge not worth a prod nav re-seed

---

## Navigation / Menu Layout

*The admin nav chrome — the top-menu vs side-menu layout toggle. Ann's pre-release walkthrough (2026-07-27).*

### AM-025: [Admin nav] The "switch to side menu" toggle is barely visible in top-menu mode; make it obvious, and pluralize both toggle labels to "menus"
- **Topic**: Admin navigation chrome → top-menu ↔ side-menu layout toggle (`client-menu.component.html`, `toggleNavLayout()`)
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Where** — two toggle buttons, one per layout:
  - **Side → Top** (shown in side-rail mode, at the top of the rail): icon `bi-menu-button-wide`, label "Switch to top menu" ([client-menu.component.html:20-23](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/layouts/components/client-menu/client-menu.component.html#L20)).
  - **Top → Side** (shown in top-menu mode, rendered as the **last pill** after all the menu pills): icon `bi-layout-sidebar`, label "Switch to side menu" ([client-menu.component.html:186-189](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/layouts/components/client-menu/client-menu.component.html#L186)).
- **Two parts:**
  1. **Make the "switch to side menu" toggle apparent in top-menu mode.** When the user switches to the top menu, the control to go back to side menus is a subtle `.pill-nav__layout-toggle` pill at the far right — **barely visible.** Ann: make it more obvious — e.g. give it a distinct/accented treatment (or a labeled button) so it clearly reads as the "back to side menus" control **right after the last menu** pill, rather than blending in with the menu items.
  2. **Pluralize both labels to "menus".** Change both the `aria-label` and `title` on each toggle: "Switch to top menu" → **"Switch to top menus"** ([:21](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/layouts/components/client-menu/client-menu.component.html#L21)) and "Switch to side menu" → **"Switch to side menus"** ([:187](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/layouts/components/client-menu/client-menu.component.html#L187)).
- **For Todd**: (1) restyle `.pill-nav__layout-toggle` (and/or add a short label) so the side-menu switch stands out at the end of the pill nav; (2) update the four strings (aria-label + title on both buttons) to the plural "menus".
- **RESOLUTION (Todd go, 2026-07-28)**: top-mode toggle now carries an always-visible **"Side menus"** label beside the `bi-layout-sidebar` icon plus an accented treatment (primary-tinted background + border, full-opacity icon, hover deepen, focus-visible shadow — all palette vars) so it reads as a layout control, not another menu pill. All four strings pluralized: "Switch to top menus" / "Switch to side menus" (aria-label + title on both toggles). `client-menu.component.html/.scss`.
- **Severity**: UX (discoverability of the layout toggle + label wording)
- **Status**: FIXED — coded 2026-07-28, awaiting Todd verify (top-menu mode → accented labeled control at strip end; tooltips read "menus") + Ann next pass

---

## Search | Registrations

*The admin Search → Registrations results grid. Ann's pre-release walkthrough (2026-07-27).*

### AM-026: [Search | Registrations] Large job (10,606 players) — add an "All"/larger page option, fix 5-digit "#" clipping, and widen Assignment
- **Topic**: Search → Registrations results grid (`search-registrations.component`)
- **Source**: Ann's pre-release walkthrough (2026-07-27) — tested on **LFTC 2026** (10,606 players)
- **Three parts:**
  1. **No "All"/large page size — capped at 1000/page.** The grid is server-side paged with `pageSizes: [100, 500, 1000]` ([search-registrations.component.ts:234](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.ts#L234)); a code comment notes there's deliberately **no "All"** because ~10K rows would be a DOM bomb without virtualization ([:233](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.ts#L233)). On LFTC 2026 (10,606 players) Ann can only see 1000 at a time and finds it unclear how to get past the first ~11 pages (11×1000 does cover all 10,606, but there's no single-view option). **Ann's request: add an ALL option.** *For Todd:* to offer an "All" (or a much larger page like 2500/5000) safely, enable Syncfusion **row virtualization** (`enableVirtualization`) so big pages render without the DOM bomb; then add the larger option(s)/All to `pageSizes`. Alternatively/additionally provide an **Export-all** path for the full set, and make the pager clearer that all rows are reachable across pages.
  2. **5-digit numbers clipped in the "#" column.** The row-number `#` column is `width="50"` ([:758](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L758)); once the index reaches 5 digits (10,000+) the number overflows and shows e.g. "106…". *For Todd:* widen the `#` column (≈50 → 70) so 5-digit row numbers fit.
  3. **Assignment column truncated — room to show full names.** The `assignment` column is `width="220"` ([:823](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L823)) and cuts off longer assignment/team names, though there's horizontal room in most cases. *For Todd:* widen the Assignment column (or let it size to content) so full assignment names show.
- **RESOLUTION (Todd go, 2026-07-28)**:
  1. **"All" page option — WON'T DO, working as designed, and the full set is already one click away.** The 1000/page cap is deliberate (10K rows × ~15 columns = DOM bomb without virtualization; retrofitting ej2 virtualization onto a grid that already combines server-side custom paging + frozen columns + checkbox persist-selection + template columns is too risky pre-go-live). **The Excel export already exports ALL matching rows** — `exportExcel()` re-runs the search unpaged and exports the entire match, not the visible page. Ann: on LFTC 2026, Export → full 10,606-row spreadsheet. The pager also shows the total item/page count, so all rows are reachable in-grid across pages.
  2. **`#` column**: width 50 → **70** — 5-digit row numbers fit.
  3. **Assignment column**: width 220 → **280** — grid pans horizontally, so the extra width costs nothing.
- **Severity**: UX (large-job usability: paging cap + two column-width clips)
- **Status**: FIXED (parts 2+3; part 1 working-as-designed w/ export-all pointer) — coded 2026-07-28, awaiting Todd verify + Ann next pass (check row 10,000+ shows full number; long assignment names; Export on LFTC 2026 returns all rows)

### AM-027: [Search | Registrations] ARB Health lives here AND as its own menu item — remove it from Search to end the two-places confusion
- **Topic**: Search → Registrations screen vs the dedicated ARB Health page
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Finding (verified)**: The Search/Registrations screen carries a full **"ARB Health" section** ([search-registrations.component.html:491-530](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L491), gated by `showArbSection()`): an **ARB Health filter** dropdown ([:498-511](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L498)) and a **"Look up CCs expiring this month"** action ([:512-530](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L512)). That duplicates the dedicated **ARB Health page** (separate menu item; the one reworked under PL-055/056/057). Having ARB Health in **two places** is confusing.
- **Ann's call**: it's not needed here — the dedicated ARB Health page is the canonical home; remove the ARB Health section from Search/Registrations.
- **⚠️ Extra reason to remove**: the "Look up CCs expiring this month" button here fires a **live Authorize.Net PRODUCTION query** — the very same expensive live lookup that PL-055 just made **click-only** on the ARB Health page to stop it auto-running. Two entry points to that prod query is both confusing and a foot-gun.
- **For Todd**: remove the ARB Health **section** (the `showArbSection()` block: ARB Health filter dropdown + the CCs-expiring lookup action + its `arbCardExpiringMode` chip/plumbing) from Search/Registrations, leaving the ARB Health page as the single home.
- **KEEP (Ann, 2026-07-27)**: leave the plain **"ARB Subscription" status multiselect filter** ([:424-431](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L424)) here in Search/Registrations — that's an ordinary grid filter (filter registrants by subscription status), not the duplicated ARB Health tooling. Only the ARB **Health** section is removed.
- **ADD a "Subscription #" search → filed as PL-059 (Ann, 2026-07-27)**: keep the existing "Invoice #" filter and add a new "Subscription #" search (invoice # = per-charge `AdnInvoiceNo`; subscription # = ARB `AdnSubscriptionId` — different identifiers). Moved to the Payment-Test punchlist as **PL-059** since it's an ARB/payment feature.
- **DECISION — should the ARB filters show only when ARB is enabled? (Ann, 2026-07-27)**: the **"ARB Subscription"** filter lives in the **"Billing"** section ([search-registrations.component.html:419-441](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L419)), which is a plain `section-card` with **no gate today** — so it shows on every job even though ARB only applies to some (tournament/ARB-enabled) sites. **Decide: leave it always visible, or show it only when ARB is turned on for that job.** *For Todd*: if gating, wrap the ARB-Subscription form-group (and the new **Subscription #** field from PL-059) in an "ARB enabled for this job" condition — check `jobFlags()` for an ARB-enabled signal, or mirror the gating pattern the ARB Health section uses (`showArbSection()`). **Same decision applies to the Subscription # filter (PL-059)** — whatever gating lands here should cover both.
- **RESOLUTION (Todd, 2026-07-28) — WON'T DO the removal; the two homes are not duplicates, they split MONITOR vs ACT:**
  - The **ARB Health page** is read-only monitoring (status tabs + click-only refresh) with **no email capability**.
  - The **Search section is the collection workflow**: the ARB Health filter drives the preset ARB dunning email templates (`email-templates.ts` — they only light up when the filter is set), and the CC-expiring lookup feeds `isCardExpiringMode` into the batch-email modal so a director can email exactly the families whose cards die this month before the next auto-bill fails. Removing the section removes the only path to *contact* ARB-behind / expiring-card families.
  - Foot-gun assessment: both prod-query entry points are click-only and human-initiated (that's what PL-055 fixed on the ARB page; the Search button always was). Two doors, each with an action behind it.
  - Post-go-live evolution, if the two-places confusion persists: add batch-email to the ARB Health page *first*, then retire the Search section — never remove the action half first.
  - **Sub-decision (gate ARB Subscription filter to ARB-enabled jobs): left as-is for now** — filter remains visible on all jobs; may revisit with PL-059 (Subscription # search) since the same gating would cover both.
- **Severity**: UX (duplicated feature / confusion) + prod-query foot-gun
- **Status**: WON'T DO (Todd agreed, 2026-07-28) — monitor-vs-act split recorded for Ann; both homes stay

### AM-028: [Search | Registrations] "Roster Scan" is tournament-only — gate it to tournament jobs, or label it "(for tournaments ONLY)"
- **Topic**: Search → Registrations filters → **Roster Scan** section
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Finding (verified)**: The **Roster Scan** section ([search-registrations.component.html:542-544](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L542)) — the "Rostered Players ≤ N" filter — is a plain `section-card` with **no job-type gate**, so it renders on **every** job even though it's only meaningful for **tournaments**. (Contrast the ARB section right above it, which is gated by `@if (showArbSection())`.)
- **Ann's two acceptable fixes (either is fine):**
  1. **Gate it to tournament jobs** — wrap the section in a tournament job-type condition, mirroring the `showArbSection()` gating pattern used for the ARB section, so it only appears on tournaments.
  2. **Or simplest — label it.** Change the section header ([:544](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.html#L544)) from "Roster Scan" to **"ROSTER SCAN (for tournaments ONLY)"** so its scope is obvious even if it stays visible everywhere.
- **For Todd**: option 1 (gate) is the cleaner outcome; option 2 (label) is the quick win if a clean tournament-only signal isn't readily at hand on this component.
- **RESOLUTION (Todd, 2026-07-28) — WON'T FIX: Roster Scan stays visible on ALL job types.** Todd's ruling: the premise is wrong — a thin-roster scan **can be useful outside tournaments** (leagues/clubs with team structures also chase under-filled teams), so neither the gate nor the "(for tournaments ONLY)" label is wanted. The Search/Teams legacy-parity addition is not pursued either (it would require a new team-search backend filter, not a UI copy).
- **Also — add Roster Scan to Search/Teams too (Ann, 2026-07-27, legacy parity)**: In **Legacy**, Roster Scan appeared in **both** Search/Registrations **and** Search/Teams. In the new version it's only in Search/Registrations. **Consider adding it under Search/Teams as well** — that's a natural home, right next to the **LADT search** already there (the "Hierarchy" section with `<app-ladt-tree-filter>`, [search-teams.component.html:203-247](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/teams/search-teams.component.html#L203)) — since a director scanning rosters would reach for it alongside the LADT tree filter. (Verified: Search/Teams has the LADT tree filter but no Roster Scan section today.) If added there and it's tournament-only, apply the same gate/label decision from this item.
- **Severity**: UX (feature shown on jobs where it doesn't apply) + Legacy-parity (missing from Search/Teams)
- **Status**: WON'T FIX (Todd, 2026-07-28) — useful beyond tournaments; no gate, no label, Search/Teams add not pursued

---

## Search | Teams

*The admin Search → Teams screen, incl. its Hierarchy / LADT tree filter. Ann's pre-release walkthrough (2026-07-27).*

### AM-029: [Search | Teams + Search | Registrations] LADT tree filter — tint the TEAMS count light blue so it stands out from Players
- **Topic**: The LADT tree filter (Hierarchy section) — the Club/AgeGroup/Division/Team (and League/A/D/T) tree with per-node Teams + Players counts
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Request (Ann)**:
  1. Under **Search/Teams → Hierarchy → Club/A/D/T**, add **light blue** color to the **TEAMS** count/column so it's easy to differentiate from Players and the team numbers stand out.
  2. Same under **League/A/D/T**, and for **both** search types (Search/Teams **and** Search/Registrations): the color goes on the **team numbers, not the players**.
- **Finding (verified)**: the tree renders per-node badges as `<span class="tree-badge" title="Teams">{{ node.teamCount }}</span>` and `title="Players"` for playerCount, plus "Teams"/"Players" column headers — **both badges currently share the same `.tree-badge` style, no color distinction** ([ladt-tree-filter.component.ts:59-61, 82-124](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared/components/ladt-tree-filter/ladt-tree-filter.component.ts#L59)). This shared component backs the Search/Teams Hierarchy filter; Search/Registrations has its own copy at [views/search/registrations/components/ladt-tree-filter.component.ts](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/ladt-tree-filter.component.ts).
- **For Todd**: give the **Teams** badge (and its column header) a distinct **light-blue** treatment (e.g. a `.tree-badge--teams` modifier with a light-blue background/text via a design-system token — no hardcoded hex) so team numbers pop and read as different from Players; leave the Players badge as-is. Apply consistently in **both** LADT tree filter components (shared → Search/Teams, and the Search/Registrations copy) so both search screens match, for both Club-rooted and League-rooted trees.
- **Severity**: UX (scannability — team vs player counts not visually distinct)
- **Status**: WON'T FIX (Todd, 2026-07-28) — not critical. Noted for the record: in the Search/Registrations copy the **Players** badge already carries the agegroup color (blue fallback), so a light-blue Teams badge would collide with blue/unset agegroups; if this is ever revisited, distinguish by FORM (outlined Teams pill vs solid Players fill), not by another hue.

---

## Job Settings

*Configure → Job Settings tabs (General, Player, Teams, Coaches, Payment, Scheduling, Branding, Communications, Mobile & Store). Ann's pre-release walkthrough (2026-07-27), reviewed for both SuperUser and Director.*

### AM-030: [Job Settings → General] Reorder fields — Customer after ADN Invoice Prefix (first row); Billing Type up next to Job Type (second row)
- **Topic**: Configure → Job Settings → **General** tab (SuperUser section field layout)
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Where**: `general-tab.component.html` — first SU row: Job ID / **ADN Invoice Prefix** ([:54-55](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L54)) / Job Path / Description; second SU row: Admin Expiry / Job Code / QBP Name / Sport / **Job Type** ([:108-109](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L108)) / **Customer** ([:119-120](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L119)) / **Billing Type** ([:130-131](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/general-tab.component.html#L130)).
- **Request (Ann)**:
  1. **Move the Customer field into the first row, right after ADN Invoice Prefix.**
  2. **Move Billing Type up into the second row, right after Job Type** (today it trails Customer and wraps awkwardly).
- **Net second-row order after the move**: Admin Expiry / Job Code / QBP Name / Sport / Job Type / **Billing Type** (Customer having moved to row 1).
- **For Todd**: relocate the Customer `col` to immediately after ADN Invoice Prefix in the first SU row, and the Billing Type `col` to immediately after Job Type in the second SU row; adjust the `col-md-*` widths so each row fits 12 units cleanly (first row gains a field, second row loses one). **Preserve the existing SuperUser/Director role-visibility** on each field while reordering.
- **Severity**: UX (field ordering / layout tidiness on the General tab)
- **Status**: Open (Ann, 2026-07-27)

### AM-031: [Job Settings → Communications] "Turn off CC & BCC copies" — should it be PLAYER-specific (keep Club Rep copies)? + SuperUser-only
- **Topic**: Configure → Job Settings → **Communications** tab → the "TURN OFF CC & BCC copies on all registration confirmations" checkbox (`bDisallowCcplayerConfirmations`)
- **Source**: Ann's pre-release walkthrough (2026-07-27); relates to/overtakes **AM-010** (the earlier "for tournaments" label item on this same checkbox) and touches **AM-018**/CR-012 (player-confirmation director copy) and **AM-011**/CR-063 (Club Rep confirmation).
- **Current state (verified)**:
  - The checkbox exists on the Comms tab with label **"TURN OFF CC & BCC copies on all registration confirmations"** and help *"Stops the CC/BCC copies… (all roles, all registration types). Registrants always still receive their confirmation. Typically used on high-volume events…"* ([communications-tab.component.html:46-58](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L46)).
  - **Behavior today = ALL roles.** `JobConfirmationCopies.Apply` *"kills CC and BCC on every confirmation regardless of role — matching legacy"* ([JobConfirmationCopies.cs:22-25](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/Email/JobConfirmationCopies.cs#L22)); `IJobRepository` doc: *"suppresses CC and BCC on every registration confirmation"* ([:508-509](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Repositories/IJobRepository.cs#L508)).
- **🔴 Ann's decision / concern**:
  1. **Should this be PLAYER-specific, not all-roles?** The **field name `bDisallowCcplayerConfirmations` ("CCplayer")** and Ann's recollection say it was **player-specific in Legacy** — but the current code applies it to *every* role and *claims* that's legacy. **These disagree → verify actual Legacy behavior and decide scope.**
  2. **Preserve Club Rep confirmation copies.** Directors of **tournaments still want to receive Club Rep confirmation copies** — so an all-roles suppression is wrong for them. Whatever scope lands must **keep Club Rep (team-registration) confirmation copies flowing** to the office.
  3. **Coach too? (maybe.)** Ann: possibly allow turning off **Coach** confirmation copies as well — i.e. Player (+ optionally Coach), but **not** Club Rep. Decide whether it's one switch (player-only) or per-role granularity.
  4. **Used most in tournament settings** — context for the default/placement.
  5. **SuperUser-only visibility.** Ann wants this checkbox shown **only to SuperUser** (confirm/ensure — it currently renders in a plain `col-12` with no `isSuperUser` gate).
  6. **Add spacing above the checkbox so it's noticed (Ann).** The checkbox sits directly under the email-list fields (the `col-12` at [communications-tab.component.html:46](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/communications-tab.component.html#L46)) and blends in. Add extra top padding/margin between the field above and this checkbox so it stands out as its own control.
- **For Todd**: (a) confirm what Legacy actually suppressed (player-only vs all roles); (b) re-scope `JobConfirmationCopies.Apply` so the switch does **not** suppress **Club Rep** confirmation copies — narrow to Player (and decide on Coach), matching the field name's "player" intent; (c) fix the label/help so they honestly describe the final scope (the current "all roles, all registration types" copy is what Ann is disputing); (d) gate the checkbox to SuperUser-only. This supersedes AM-010's "for tournaments" label concern.
- **Severity**: 🔴 Bug/behavior (over-broad suppression kills Club Rep copies tournament directors rely on) + role-visibility
- **Status**: Open — verify Legacy + Todd decision (Ann, 2026-07-27)

### AM-032: [Job Settings → Player/Teams/Coaches/Scheduling/Mobile] Highlight the "managed in Quick Links — read-only" info note so directors notice it
- **Topic**: Configure → Job Settings tabs — the "Registration & public-visibility switches are managed in Quick Links. They appear here read-only." info banner
- **Source**: Ann's pre-release walkthrough (2026-07-27), spotted on the **Players** tab
- **Where**: `job-config.component.html:34-41` — the shared **`.quick-links-note`** banner: `<i class="bi bi-info-circle">` + *"Registration & public-visibility switches are managed in [Quick Links]. They appear here read-only."* It's gated by `showQuickLinksNote()`, which is true on **all** the Quick-Links-managed tabs — **Player, Teams, Coaches, Scheduling, Mobile & Store** ([job-config.component.ts:69-71](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/job-config.component.ts#L69)).
- **Request (Ann)**: this info text is **too easy to miss** — directors **used to set these switches right here**, so they need to notice that these are now read-only and live in Quick Links. **Make it stand out**: bold and/or colored text, and/or a more attention-grabbing icon (change the plain info-circle).
- **For Todd**: strengthen the shared `.quick-links-note` callout — e.g. **bold the key phrase** ("managed in **Quick Links**" / "**read-only**"), give it an accent/warning treatment (colored border or tinted background via a design-system token — no hardcoded hex), and/or swap `bi-info-circle` for a more noticeable icon (e.g. `bi-exclamation-triangle` / `bi-lightbulb`). Because it's the **shared** banner, one change lifts all five tabs at once. Keep WCAG AA contrast.
- **Also — place the note INSIDE the registration cards (Ann, 2026-07-27)**: the same "managed in Quick Links, read-only" note also appears under **Job Settings → Teams**. Beyond the top-of-tab banner, **consider putting the highlighted note directly within the Player Registration card (Player tab), the Team Registration card (Teams tab), AND the Adult Registration card (Coaches tab)** — i.e. right next to the now-read-only activate/inactivate switches — so a Director trying to toggle registration on/off sees the pointer **at the control** and follows the new Quick Links path. (The top banner is easy to scroll past; the in-card note lands exactly where they'd expect to flip the switch.) Applies to each registration type's card — Player, Team, **and Adult**.
- **Severity**: UX (discoverability — directors miss that these switches moved to Quick Links)
- **Status**: Open (Ann, 2026-07-27)

### AM-033: [Job Settings → Player] Text boxes (RTE editors) are too small — enlarge for reading/editing; + Refund Policy still needs to move to Payment
- **Topic**: Configure → Job Settings → **Player** tab — the Confirmation/Waiver/Code-of-Conduct/Refund rich-text boxes
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Two parts:**
  1. **Refund Policy → Payment (already tracked — cross-ref).** As recommended before, the Player tab's **Refund Policy** should move to the **Payment** tab. This is already captured in **AM-009 item 1** (Payment-tab refund-policy relocation, job-type-aware; from ConfigureMenus PL-044) — Ann re-confirms it here; no separate item needed, just make sure it lands.
  2. **Text boxes are too small (new).** The Player-tab RTE editors — Confirmation Email, Confirmation On-Screen, Refund Policy, Release of Liability, Code of Conduct, COVID Waiver ([player-tab.component.html:99-156](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L99)) — all share `[height]="rteHeight"` = **`JOB_CONFIG_RTE_HEIGHT = 200px`** ([rte-config.ts:11](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/shared/rte-config.ts#L11)). At 200px they're **way too small** to read/edit the multi-paragraph legal text in one glance.
- **For Todd**: increase the RTE height so more content shows at a glance — bump `JOB_CONFIG_RTE_HEIGHT` (e.g. 200 → ~350-400px) and/or make the editors **resizable** (drag handle / auto-grow). **Note it's a shared constant** — raising it also enlarges the **Coaches** tab RTEs (same fields) and any other job-config HTML field using it, which is desirable here; if you'd rather not touch all of them, override the height just on these Player/Coaches confirmation/waiver editors.
  - **Exception (Ann, 2026-07-27): keep the COVID Waiver editor small.** It's only used by **one client**, so it shouldn't get the taller treatment — give the COVID Waiver its own smaller height override while the other Player-tab editors grow ([player-tab.component.html:150-156](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L150)).
- **Severity**: UX (editors too small to read/edit comfortably)
- **Status**: Open (Ann, 2026-07-27)

### AM-034: [Job Settings → Player → Player Settings (SuperUser)] Retire Mom/Dad Label if unused (moved to Contact 1/2); reposition the Offer RegSaver Insurance checkbox
- **Topic**: Configure → Job Settings → **Player** tab → **Player Settings (SuperUser)** section
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Two parts:**
  1. **Are "Mom Label" / "Dad Label" still used? Remove if not.** Registration/Family-Account now uses **"Contact 1" / "Contact 2"** (PlayerReg PL-050), so Ann asks whether the per-job **Mom Label** ([player-tab.component.html:80-83](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L80)) and **Dad Label** ([:86-89](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L86)) config fields are still needed. **Verified (2026-07-27) — NOT fully dead:** `momLabel`/`dadLabel` are still saved ([JobConfigService.cs:289-290](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobConfigService.cs#L289)), **read into** the registration-detail DTO (default "Mom"/"Dad", [RegistrationRepository.cs:2395-2396](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L2395)) and **job metadata** ([JobsController.cs:97-98](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/JobsController.cs#L97)), and cloned ([JobCloneService.cs:969-970](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/JobCloneService.cs#L969)). So they still flow through the app even as the UI moves to Contact 1/2. **For Todd — decide**: finish the Contact-1/2 migration on the surfaces that still honor these labels, then **remove the Mom/Dad Label config fields + their DTO/metadata/clone plumbing**; OR keep them if any surface should still allow custom parent labels. (Not a one-line UI delete — audit the consumers first.)
     - **Recommendation (Ann, 2026-07-27): populate all of them with "Contact 1" / "Contact 2" instead.** Rather than keep "Mom"/"Dad", change the defaults/consumers (the `?? "Mom"` / `?? "Dad"` fallbacks and any surface that renders these labels) to **"Contact 1" / "Contact 2"** so everything matches the registration side — then the per-job Mom/Dad Label config fields can be retired.
  2. **Reposition the "Offer RegSaver Insurance" checkbox.** It currently sits at the top of the section ([player-tab.component.html:72-77](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L72)). Ann: **move it down and add space above it (under the field above) so it's noticed.** *Aside for Todd:* it's currently `[disabled]="true"` — confirm whether that's intended (RegSaver offered by default / controlled elsewhere) and whether "make it noticed" also means it should be enabled, or it's a read-only indicator.
- **Severity**: UX (vestigial-vs-live label fields + checkbox placement)
- **Status**: Open (Ann, 2026-07-27)

### AM-035: [Job Settings → Teams] Roster Visibility — Ann still wants it SPLIT onto the Adult and Player tabs (re-raise of AM-013)
- **Topic**: Configure → Job Settings → **Teams** tab → Roster Visibility toggles
- **Source**: Ann's pre-release walkthrough (2026-07-27) — re-raise; see **AM-013** (from ConfigureMenus PL-058)
- **Request (Ann)**: The **Roster Visibility** toggles should be **split and placed under the Adult (Coaches) and Player tabs** — the player roster-view toggle on the **Player** tab, the adult/coach roster-view toggle on the **Coaches** tab — rather than both living on the Teams tab. (Ann: "I think I may have mentioned this before" — yes, this is the original AM-013 ask.)
- **⚠️ Tension to reconcile — Todd implemented the opposite**: Todd's recent commits resolved AM-013 as **"roster-visibility toggles get descriptive labels + help, STAY on Teams"** (plus copy cleanups: dropped club reps from the adult roster-view description, removed a wrong tournament clause, trimmed help lines since labels suffice). So today they're **kept on the Teams tab** with clearer labels — **not** split. Ann is re-requesting the split.
- **For Todd + Ann to decide**: keep-on-Teams-with-labels (Todd's shipped call) vs. split-to-Player/Coaches (Ann's preference). If splitting: move `bAllowRosterViewPlayer` → Player tab and `bAllowRosterViewAdult` → Coaches tab (DB columns unchanged, only DTO/service mapping shifts — see AM-013 for the exact plumbing). This is a placement disagreement, not a behavior bug — needs a joint call.
- **Severity**: UX (field placement — Director expectation) — decision pending
- **Status**: Open — reconcile with Todd's shipped AM-013 "stay on Teams" decision (Ann, 2026-07-27)

### AM-036: [Job Settings → Teams] Club Rep Permissions — confirm they migrate from Legacy at go-live (keep Shoulberg tournaments as requested)
- **Topic**: Configure → Job Settings → **Teams** tab → **Club Rep Permissions** (Allow Add / Edit / Delete)
- **Source**: Ann's pre-release walkthrough (2026-07-27) — migration / go-live question
- **Where / fields**: `teams-tab.component.html:45-64` — **Allow Edit** (`bClubRepAllowEdit`), **Allow Delete** (`bClubRepAllowDelete`), **Allow Add** (`bClubRepAllowAdd`) — per-job boolean columns on `Jobs`.
- **Question (Ann)**: when we **migrate and go live**, how are these handled — **will the Club Rep Permission inputs transfer from Legacy?** We need to **keep all Shoulberg tournaments as requested before** (their existing Club Rep permission settings must be preserved).
- **Why it matters / nuance**: these permission switches are **now enforced server-side** in the new system (per ChelseaReview CR-006 — "Club-rep add/edit/delete permission switches now enforced server-side"). So it's not just "do the values copy" — even if the columns carry forward, the **new enforcement** means a value that may have been cosmetic/loosely-honored in Legacy is now actively gating what Club Reps can do. If the values *don't* carry (or default), Shoulberg's club reps could suddenly gain or lose add/edit/delete rights at go-live.
- **For Todd (migration owner)**: (1) confirm the Legacy → new migration **maps `bClubRepAllowAdd/Edit/Delete` from the Legacy source** (not defaulted); (2) **spot-check the Shoulberg tournaments** post-migration to verify their Club Rep permissions match what was requested/agreed; (3) confirm the new server-side enforcement produces the intended behavior for those carried-forward values.
- **Also — relabel the tab "Teams" → "Club Reps/Teams" (Ann, 2026-07-27)**: since this tab holds Club Rep settings/permissions (not just teams), Ann finds it clearer to label the tab **"Club Reps/Teams"**. **⚠️ Re-raise — previously declined**: the ConfigureMenus sweep filed this exact rename and it was **"Closed — won't change" (leave labeled 'Teams')**. Ann is re-requesting it, so it needs reconciling with that earlier decision rather than treated as new.
- **Severity**: Migration / go-live data integrity (client-specific: Shoulberg) + tab-label clarity (re-raise)
- **Status**: Open — Todd to confirm at migration; tab-rename needs reconciling with prior "won't change" (Ann, 2026-07-27)

### AM-037: [Job Settings → Coaches/Adult] Move Refund Policy to Payment; then Release of Liability moves under Confirmation On-Screen and gets more room
- **Topic**: Configure → Job Settings → **Coaches (Adult)** tab → "COACH Confirmation, Liability Waiver & Code of Conduct Text" section
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Current order** ([coaches-tab.component.html:109-159](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L109)): Confirmation Email → Confirmation On-Screen → **Refund Policy** (`adultRegRefundPolicy`, [:132-139](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L132)) → **Release of Liability** (`adultRegReleaseOfLiability`, [:142-149](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/coaches-tab.component.html#L142)) → Code of Conduct.
- **Request (Ann):**
  1. **Move Refund Policy off this tab to the Payment tab** — already tracked in **AM-009 item 1** (refund policy relocated to Payment, **distinguishing the Player vs Club Rep/Team fields there** — the "Adult" `adultRegRefundPolicy` becomes the "Club Rep / Team Refund Policy" on Payment). Cross-ref, no dup; this is the Adult-tab side of that move.
  2. **Release of Liability then sits right under Confirmation On-Screen.** This happens **automatically** once Refund Policy is removed (order becomes Confirmation Email → Confirmation On-Screen → **Release of Liability** → Code of Conduct) — which is exactly the placement Ann wants.
  3. **Give Release of Liability more room.** With Refund Policy gone there's space to enlarge the Release-of-Liability editor. Ties to **AM-033** (Player/Coaches RTE editors too small; enlarge the shared `JOB_CONFIG_RTE_HEIGHT` and/or make resizable) — apply that here so Release of Liability is comfortably readable/editable.
- **For Todd**: sequence it with AM-009's refund-policy move — after `adultRegRefundPolicy` leaves the Coaches tab for Payment, confirm Release of Liability lands directly under Confirmation On-Screen and give it the enlarged RTE height from AM-033.
- **Severity**: UX (field order + editor size on the Adult tab)
- **Status**: Open (Ann, 2026-07-27)

---

## LADT

*LADT editor — the right-side per-level grids (League / Age Group / Division / Team). Ann's pre-release walkthrough (2026-07-27).*

### AM-038: [LADT editor] Optimize the right-side grid column widths to kill horizontal scrolling — esp. Team level (dates buried) and the too-wide EBD / Late Fee columns
- **Topic**: LADT editor → right-side per-level tables (League / Age Group / Division / Team)
- **Source**: Ann's pre-release walkthrough (2026-07-27) — continues the closed LADT-sweep items SP-006 / SP-023 ("right-side grids too wide"), re-raised for release
- **Request (Ann)**: The tables at **all levels** can be tightened so **horizontal scrolling is eliminated in most (not all)** of them. Specifically: the **Team level** has a lot of empty/low-value columns and should be **consolidated so the dates are visible without scrolling**; and there's **way too much real estate on EBD (Early Bird Discount) and Late Fee**.
- **Finding (verified)** — column widths live in [ladt-grid-columns.ts](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/configs/ladt-grid-columns.ts):
  - **EBD / Late Fee are wide at every level**: `_earlyBird` = **160px**, `_lateFee` = **150px** (league [:24-25], agegroup [:38-39], team [:66-67]). Ann: too much real estate — narrow these.
  - **Team level is the worst**: ~20 columns (Club 160, Team 160, Active, Players, Max Roster, Fees 220, EBD 160, Late Fee 150, Payment Phase 180, then the **Dates** group Start/End/Effective/Expires at [:70-73], then Rank, Div Requested, Last Record, LOP, Self Roster, Gender, Requests 180, Comments 180…). Because **Dates come after all the wide fee columns**, you must scroll right past them to see Start/End/Effective/Expires.
- **Cut-off headers at Team level (Ann, 2026-07-27)**: headers like **ACTIVE, PLAYERS, MAX ROSTER** look **sloppy / cut off** — these columns are narrow (`Active` 70px [:62], `Players` 75px [:63], `Max Roster` 75px [:64]) so the header text doesn't fit cleanly. Fix so headers render tidily — give these columns enough width for their header (or wrap the header at word boundaries / shorten the labels), balanced against the overall tightening. "Max Roster" at 75px is the worst offender.
- **For Todd**: tighten `ladt-grid-columns.ts` — (1) **narrow `_earlyBird` / `_lateFee`** across all levels; (2) **trim/consolidate the Team-level column set** (drop or hide rarely-used columns, and/or move the **Dates** group earlier) so Start/End/Effective/Expires are visible **without horizontal scroll**; (3) size each level's columns to content so most grids fit the panel with no horizontal scroll (Team may still need some, but far less); (4) ensure narrow headers (Active/Players/Max Roster) aren't clipped — width-to-header or clean wrap. Keep the frozen first column(s) so identity stays visible while scrolling any remainder.
- **Severity**: UX (grid density — horizontal scrolling / buried dates across LADT levels)
- **Status**: Open (Ann, 2026-07-27)

---

## Teams & Rosters

*The Teams & Rosters menu section (Roster Swapper, Check-In, Camp Groups, etc.). Ann's pre-release walkthrough (2026-07-27).*

### AM-039: [Teams & Rosters → Roster Swapper] Highlight just-moved player(s) on their new team — no confirmation, easy to lose track / make errors
- **Topic**: Teams & Rosters → **Roster Swapper**
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **What happens (verified)**: Selecting the arrow moves the player **immediately** — `executeSwap` fires the transfer, shows a **3-second success toast**, then reloads both rosters and clears the selection ([roster-swapper.component.ts:306-334](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/roster-swapper/roster-swapper.component.ts#L306)). **There is no confirmation** before the move, and **no highlight** of the moved player afterward — once the rosters reload, the only trace was the transient toast.
- **Why it matters (Ann)**: the move is instant and it's **easy to forget who you just moved** — and with **no confirmation step, errors are easy to make**. If you moved the wrong player, you need to spot them on the new team to move them back.
- **Request (Ann)**: after a move, **highlight the just-moved player on their new (target) team** so you can see who was moved and move them back if it was a mistake. **Same for multi-select** — if several players were moved at once, **highlight all of them** on the target roster.
- **For Todd**: after a successful transfer, mark the moved `registrationIds` (single **and** batch) and apply a **highlight** to those rows on the target roster once it reloads — e.g. a temporary highlighted/"just moved" row style (and/or a small badge) that persists until the next action (or fades after a bit). There's already a `swappingId` signal for the single in-flight case; extend to a `justMovedIds` set used to style target rows post-reload. (Optional, given "no confirmation": consider an easy inline "undo/move back" affordance on the highlighted rows.)
- **Severity**: UX (error recovery — no confirmation + no post-move visibility on an instant, mistake-prone action)
- **Status**: Open (Ann, 2026-07-27)

### AM-040: [Teams & Rosters → Pool Assignment] Transfer confirmation — Direction is incorrect when the left-pointing (target→source) move is used
- **Topic**: Teams & Rosters → **Pool Assignment** → Transfer Preview / Confirm Transfer
- **Source**: Ann's pre-release walkthrough (2026-07-27)
- **Where**: `pool-assignment.component.html` — "Target Agegroup:Division" ([:231](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/pool-assignment/pool-assignment.component.html#L231)); the two move buttons (right = source→target [:222], left = target→source [:420]); Transfer Preview panel + per-row **Direction** column ([:458, 470-473](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/pool-assignment/pool-assignment.component.html#L458)); **Confirm Transfer** button ([:516-523](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/pool-assignment/pool-assignment.component.html#L516)).
- **What Ann saw**: The confirmation text at the bottom is **great** — **but** when the **left-pointing arrow (target→source) move** is selected, the confirmation's stated **Direction is incorrect**. (Relates to **PL-042**, which fixed the per-row Direction *arrow* to point right for source→target and left for target→source — this appears to be a remaining direction gap on the **confirmation/summary text** for the left/target→source case.)
- **For Todd**: check how the confirmation summary derives its direction for the **target→source** (left-arrow) flow — the per-row arrows were fixed under PL-042, but the confirmation Direction wording still reads wrong for a left-arrow move. Make the confirmation Direction match the actual move direction in both flows.
- **⏳ Part 2 pending Ann's completion**: Ann's note continued *"Also, the Confirm Transfer not…"* but was cut off — **awaiting the rest** (likely something about the Confirm Transfer button state/behavior). To be filled in.
- **Severity**: UX / possible Bug (misleading confirmation direction on target→source moves)
- **Status**: Open — part 2 pending Ann (Ann, 2026-07-27)
