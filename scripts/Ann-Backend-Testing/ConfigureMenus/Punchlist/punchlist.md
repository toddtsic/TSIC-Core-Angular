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
- **Status**: Open
- **Note**: Apply the Nav Editor layout pattern as the standard for every Configure page (Administrators, Customers, Customer Groups, Discount Codes, Age Ranges, Dropdown Options, Theme, Widget Editor, Job Clone, Report Catalogue, Job Settings). Ideal fix: promote the `max-width: 1200px; margin: 0 auto; padding: 2rem;` rule to a shared `.configure-page` utility (or reuse an existing one if already in `_utilities.scss`) so every Configure page inherits the same bounds — and future Configure pages pick it up automatically. Verify on wide monitors that the grid columns still breathe at 1200px; if not, revisit the cap.

### PL-021: Configure submenu items no longer in alphabetical order — confirm intended ordering logic
- **Area**: Menu Editor
- **What I did**: Opened the Configure dropdown and scanned the submenu items
- **What I expected**: Alphabetical ordering as was the case previously, so items are predictable to find
- **What happened**: Items appear in this order: Job Settings, Discount Codes, Age Ranges, Administrators, Customer Groups, Dropdown Options, Customers, Theme, Nav Editor, Widget Editor, Job Clone, Report Catalogue. Looking at `scripts/5) Re-Set Nav System.sql:117-128`, the list is grouped by role visibility — the first three are Director-visible and the rest are SuperUser-only — so there *is* a grouping rationale, but within each group the ordering is not alphabetical.
- **Severity**: Question
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
- **Note**: Timezones is a small DB lookup table ([Timezones.cs](TSIC-Core-Angular/src/backend/TSIC.Domain/Entities/Timezones.cs)) — `TzId`/`TzName`/`UtcOffset`/`UtcOffsetHours`/audit fields. No seed script in repo; rows carried from Legacy. Fix (if Arizona is indeed missing — couldn't enumerate rows from codebase): one-row INSERT into `reference.Timezones`, e.g. `('US Arizona Time', -420, -7, NULL, GETDATE())` — Arizona uses MST year-round at UTC−7. Mirror whatever offset convention existing rows use before running.
  - **Linked to PL-015**. If PL-015 resolves as "remove the customer-level timezone field entirely," PL-018 becomes moot — resolve PL-015 first to avoid adding a row to a doomed table.
  - **DB change, not a code edit** — goes through Todd's preferred migration path (script in `scripts/`, run manually in prod).

### PL-017: Edit Customer — Timezone popup shows "Afghanistan Time" but table shows "Eastern Time"
- **Area**: Menu Editor
- **What I did**: Edited a customer and looked at the Timezone field in the popup
- **What I expected**: Timezone in the popup to match what the table shows (Eastern Time)
- **What happened**: Popup shows "Afghanistan Time" but the table reads "Eastern Time" — something is wrong with the timezone field defaulting or display
- **Severity**: Bug
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
- **Note**: **Rename** done on 2026-04-24 — h2 at [customer-configure.component.html:3](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customers/customer-configure.component.html#L3) changed from "Customer Configure" → "Customers" (nav label already says "Customers", so the page now matches). **Heading size** half defers to PL-022 / PL-007 — same family of Configure-pages-heading-size standardization; will be resolved together.

### PL-013: Customer Groups — Add and Delete buttons too far from customer names in right table
- **Area**: Menu Display
- **What I did**: Looked at the Add and Delete functions next to the customer names in the right table
- **What I expected**: Buttons positioned close to the customer names
- **What happened**: Too much space between the buttons and the customer names — move them much closer
- **Severity**: UX
- **Status**: Open
- **Note**: Cause isolated on 2026-04-24 — `.member-item` uses `justify-content: space-between` ([customer-groups.component.scss:168-172](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.scss#L168-L172)), which pins the × delete button to the far right of the 2fr-wide member panel. "Add" isn't per-row — it's a dropdown row at the top (`.add-member-row`), so this is effectively about the per-row Delete (×) placement. Fix: swap `justify-content: space-between` for `flex-start` and add `gap: var(--space-2)` so the × sits next to the customer name. Pair with PL-022 workspace pass if member-panel width is also narrowed.

### PL-012: Customer Groups — "Members of [group name]" header needs visual emphasis
- **Area**: Menu Display
- **What I did**: Clicked into a Customer Group and saw the header "Members of 'STEPS Lacrosse LLC'"
- **What I expected**: The group name to stand out visually — highlighted, bold, or styled differently
- **What happened**: Header doesn't stand out enough — needs some kind of highlighting so the group name is clearly visible
- **Severity**: UX
- **Status**: Open
- **Note**: Current markup at [customer-groups.component.html:131](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.html#L131) — `<span class="panel-title">Members of "{{ selectedGroup()!.customerGroupName }}"</span>` — group name is inline literal text inside a flat span, no emphasis. Fix: split the name into its own element and style distinctly. Options: **(A)** bold via `<strong>`; **(B)** accent color (`var(--bs-primary)` / `var(--brand-text)`); **(C)** badge-style pill after the "Members of" prefix (closest to "highlighted"); **(D)** bold + accent (B+A combination). Pick during PL-022 workspace pass so typography choice stays coherent with the shared Configure page style.

### PL-011: Customer Groups — remove total group count, add column heading like "Member Jobs" or "Jobs Included"
- **Area**: Menu Display
- **What I did**: Looked at the Customer Groups table
- **What I expected**: A meaningful column heading above the job count numbers
- **What happened**: Shows total number of groups (currently 5) which isn't useful. Remove it and instead add a heading above the numbers in each row, like "Member Jobs" or "Jobs Included", so it's clear what the numbers represent.
- **Severity**: UX
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
- **Note**: Defer to PL-022. Concrete target confirmed during walkthrough on 2026-04-24: Customer Groups uses `<h2 class="page-title">` with a local override (`font-size: 1.5rem; font-weight: 700;` per [customer-groups.component.scss:13-16](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/customer-groups/customer-groups.component.scss#L13-L16)). Other Configure pages render at three different sizes: Administrators/Widget Editor/Age Ranges/Discount Codes/Customer Configure at Bootstrap h2 default (~2rem) via `mb-0`; Job Config/DDL Options at `var(--font-size-lg)` (~1.125rem) via the global `.page-title`; Nav Editor at `<h1>`. Fix: promote Customer Groups' local override to the global `.page-title` in `_utilities.scss` (or introduce a `--page-title-font-size` token) and apply `class="page-title"` consistently to every Configure page — folds into PL-022's workspace standardization.

### PL-006: Add Administrator — how does someone become eligible in the Username search? Dropdown options seem random.
- **Area**: Menu Editor
- **What I did**: Clicked "Add Administrator" and looked at the Username search dropdown
- **What I expected**: Clear list of eligible users, or understanding of how someone becomes eligible to be added
- **What happened**: Dropdown seems to have random options — not clear how a person registers or qualifies to appear in the Username search
- **Severity**: Question
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
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
- **Status**: Open
- **Note**: Refer to PL-022 — heading sizing folds into the broader "all Configure pages use the narrow Nav Editor workspace" fix. Resolve together.

### PL-001: Administrators table — match Search/Player table style and reorder columns
- **Area**: Menu Display
- **What I did**: Opened the Administrators menu and looked at the table
- **What I expected**: Consistent look with the Search/Player menu table
- **What happened**: Needs several changes: (1) Match column heading font and style to Search/Player table, (2) Change "Status" to "Active" and move it right after the Name column with "Yes" if active, (3) Column order should be: Name, Active, Role, Username, Registered
- **Severity**: UX
- **Status**: Open
