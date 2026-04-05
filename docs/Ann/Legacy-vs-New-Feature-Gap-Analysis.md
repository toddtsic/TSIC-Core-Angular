# Legacy vs. New App — Feature Gap Analysis

**Date:** April 5, 2026  
**Purpose:** Compare TSIC-Unify-2024 (legacy) against TSIC-Core-Angular (new app) for six functional areas Ann is testing.

---

## 1. REGISTER PLAYER

| Legacy Feature | In New App? | Notes |
|---|---|---|
| Family account creation | Yes | Family wizard v2 |
| Single-player registration | Yes | Player wizard v2 |
| Multi-player per family | Yes | Player wizard handles multiple players |
| Dynamic form fields per job | Yes | Custom forms step in wizard |
| Waivers (1, 2, 3, COVID) | Yes | Waivers step in wizard |
| Credit card payment (Authorize.net) | Yes | Via payment step |
| Check/cash payment | Yes | Payment step supports it |
| Discount codes | Yes | Per-player discount application |
| Processing fee calculation | Yes | Automatic based on job flags |
| Eligibility constraints (grad year, age, club) | Yes | Dedicated eligibility step |
| Email confirmation | Yes | Confirmation controller |
| Insurance (Vertical Insure / RegSaver) | Yes | ARB integration exists |
| Recurring billing (ARB) setup during registration | **Partial** | ARB health + update-CC exist, but legacy had ARB subscription creation *during* the payment step with configurable billing occurrences and interval lengths |
| Multi-player family registration (CAC "Choose a Camp") | **Check needed** | Legacy had a distinct CAC flow where families pick *team offerings* first, then fill forms per player. New wizard does player-first then team selection. Different UX but same outcome |
| Photo/headshot upload during registration | **Not confirmed** | Legacy captured HeadshotPath during registration |
| Medical form upload during registration | **Not confirmed** | Legacy had BUploadedMedForm, BUploadedInsuranceCard, BUploadedVaccineCard file uploads inline |
| College recruitment fields (SAT, GPA, coach refs) | **Depends on form config** | Legacy had 20+ recruitment fields. These would need to be in the dynamic form system |
| Uniform/jersey size selection during registration | **Depends on form config** | Legacy captured JerseySize, ShortsSize, TShirt, etc. |

**Verdict:** Core player registration is reproduced. The edge cases depend on whether the dynamic form fields include all the legacy field types (file uploads, recruitment fields, sizing).

---

## 2. REGISTER TEAM

| Legacy Feature | In New App? | Notes |
|---|---|---|
| Club rep account login/creation | Yes | Team wizard step 1 |
| Add/edit/delete teams | Yes | Team wizard step 2 |
| Agegroup selection per team | Yes | Team metadata |
| Credit card payment | Yes | Team payment controller |
| Check/cash payment | Yes | Team payment controller |
| Discount codes (one per club rep) | Yes | Enforced in backend |
| Processing fees | Yes | Recalculate-fees endpoint |
| Refund policy waiver | Yes | Part of wizard flow |
| Email confirmation | Yes | Confirmation endpoint |
| Club management (add/remove/rename) | Yes | Dedicated endpoints |
| Conflict detection (double-reg by different reps) | Yes | check-existing endpoint |
| Insurance (Vertical Insure) | Yes | ARB integration |
| Coach management (SOCCER flow) | **Not confirmed** | Legacy had a dedicated Coaches step (add/edit/delete coaches with background checks, certifications, DOB). New wizard doesn't appear to have a coach step |
| Sport-specific flows (LAX vs SOCCER) | **Simplified** | Legacy had two separate views (LaxTournament vs SoccerTournament). New app has one unified team wizard |
| Copy AAYSA team | **Not confirmed** | Legacy had CopyAAYSATeam() to import existing teams from another system |
| Level of Play (LOP) assignment | **Check needed** | Legacy captured LOP per team during registration |
| Coach assignment to teams | **Check needed** | Legacy SOCCER flow required assigning a coach to each team |

**Verdict:** Core team registration is reproduced. The coach management step from the SOCCER flow may be missing as a distinct feature.

---

## 3. SEARCH PLAYERS / REGISTRATIONS

| Legacy Feature | In New App? | Notes |
|---|---|---|
| Text search (name, email, phone) | Yes | Full-text + filter search |
| Filter by team/agegroup/division | Yes | Advanced filtering |
| Filter by status (active, paid, role) | Yes | Filter options endpoint |
| View/edit registrant details | Yes | Inline editing |
| View payment history | Yes | Accounting ledger component |
| Edit family contact info | Yes | Dedicated PUT endpoints |
| Edit demographics | Yes | Dedicated PUT endpoint |
| Team reassignment | Yes | team-assignment endpoint |
| CADT tree filter | **Yes — NEW and BETTER** | Club/Agegroup/Division/Team ownership filter (legacy didn't have this) |
| Batch email/SMS to filtered results | **Not confirmed** | Legacy had a full email tab with substitution variables and batch sending |
| Reassign registrant to different job | **Not confirmed** | Legacy ChangeRegistrantsJob() moved registrants between events |
| Cancel subscription (ARB) | **Not confirmed** | Legacy had CancelSubscription() inline |
| View/edit subscription details | **Not confirmed** | Legacy had subscription record viewing/editing |
| Invoice number search | **Not confirmed** | Legacy could search by ADN invoice number |
| Mobile registration filter | **Not confirmed** | Legacy filtered by registration method |
| Age range filter | **Not confirmed** | Legacy had age range + grade year filtering |
| Select teams under roster threshold | **Not confirmed** | Legacy had a bulk tool to find undersized teams |
| Delete registration | **Not confirmed** | Legacy had DeleteRegistration() |

**Verdict:** Core search is reproduced with the CADT tree being a major improvement. Batch email, job reassignment, and subscription management from search results may be missing.

---

## 4. SEARCH TEAMS

| Legacy Feature | In New App? | Notes |
|---|---|---|
| Search by club, agegroup, LOP | Yes | Team search with filters |
| Edit team (name, status, LOP) | Yes | PUT endpoint |
| View team roster | Yes | Team detail view |
| Team payment recording (CC, check) | Yes | Dedicated endpoints |
| Club rep accounting view | Yes | clubrep/{id}/accounting endpoint |
| Bulk charge all teams in club | **Yes — NEW** | charge-cc-club + record-check-club |
| Transfer all teams between reps | **Yes — NEW** | transfer-all-teams endpoint |
| Change team's club | **Yes — NEW** | change-club endpoint |
| Refund processing | Yes | Refund endpoint in team search |
| Waitlist filter | **Not confirmed** | Legacy filtered waitlisted vs non-waitlisted |
| Scheduled vs non-scheduled filter | **Not confirmed** | Legacy filtered by schedule status |
| Team comments/notes | **Not confirmed** | Legacy had inline team comments |

**Verdict:** Search Teams is well-reproduced with several NEW bulk operations the legacy didn't have.

---

## 5. LADT (League / Agegroup / Division / Team)

| Legacy Feature | In New App? | Notes |
|---|---|---|
| Tree view of hierarchy | Yes | LADT editor component |
| Create/edit/delete leagues | Yes | Full CRUD |
| Create/edit/delete agegroups | Yes | With color coding |
| Create/edit/delete divisions | Yes | Full CRUD |
| Create/edit/delete teams | Yes | Full CRUD |
| Agegroup color coding | Yes | Color endpoint |
| Roster swapper (drag-drop) | **Yes — NEW** | Dedicated component, legacy didn't have this |
| Pool assignment tool | **Yes — NEW** | Dedicated component |
| Field assignment to leagues | **Not confirmed** | Legacy had HomeFields(), GetFieldTeams(), EditFieldTeams() at the league level |
| Auto-create waitlist agegroups | **Not confirmed** | Legacy AddWaitlists() auto-generated waitlist agegroups |
| Bulk update player fees to agegroup fees | **Not confirmed** | Legacy UpdatePlayerFeesToAgegroupFees() was a one-click bulk operation |
| Team clone | **Not confirmed** | Legacy Clone() duplicated teams |
| Agegroup fee configuration (base, discount, late, dates) | **Check needed** | Legacy had extensive per-agegroup fee fields |
| Max teams per club limit | **Check needed** | Legacy enforced max teams overall and per club |
| Self-rostering toggle | **Check needed** | Legacy had bSelfRoster per agegroup |
| Champions by division toggle | **Check needed** | Legacy had bChampionsByDivision per agegroup |

**Verdict:** LADT core is reproduced with roster swapper and pool assignment as major improvements. Some admin convenience features (bulk fee sync, waitlist auto-create, team clone) may need verification.

---

## 6. ACCOUNTING (Add Records, Distribute Records)

| Legacy Feature | In New App? | Notes |
|---|---|---|
| Record CC payment for player | Yes | Player payment controller |
| Record check payment for player | Yes | Via search interface |
| Record correction/adjustment | Yes | Correction payment method |
| Record CC payment for team | Yes | Team payment controller |
| Record check payment for team | Yes | Team search controller |
| Discount code application | Yes | Both player and team |
| Fee calculation (base + processing) | Yes | Automatic |
| View payment ledger | Yes | Accounting ledger component |
| Club-level bulk payment | **Yes — NEW** | Charge all teams in a club at once |
| Refund processing | Yes | Refund endpoint in team search |
| Discount code management UI | Yes | Dedicated discount codes config page |
| Admin charges (monthly/yearly) | **Not confirmed** | Legacy had JobAdminFeesController for admin-level charges by month/year |
| ARB behind-in-payment sweep | **Not confirmed** | Legacy found behind-in-payment registrants and sent sweep emails automatically |
| ARB transaction import from ADN | **Not confirmed** | Legacy ImportSweepTransactionsIntoTSIC() pulled ADN transactions in bulk |
| Family-wide payment distribution | **Not confirmed** | Legacy distributed a single CC charge across multiple family members' registrations automatically |
| Fee modifier system | **Not confirmed** | Legacy had FeeModifiers for time-based fee adjustments |
| Apply credit to player/team | **Not confirmed** | Legacy had dedicated "apply credit" views |
| Set balance due for team | **Not confirmed** | Legacy had a set-balance-due view |
| Unpaid communication templates | **Not confirmed** | Legacy had pre-built email templates for unpaid balances |
| Payment sweep emails | **Not confirmed** | Automated emails to families behind on ARB payments |

**Verdict:** Core payment recording is reproduced. The ARB sweep/import automation, family-wide payment distribution, admin charges, and fee modifier system may need verification.

---

## NEW Features That Did Not Exist in Legacy

| Feature | What It Does |
|---|---|
| **Widget Dashboard** | Three-tier configurable dashboard (platform, job, user) with live metrics, charts, year-over-year trends |
| **Navigation Editor** | WYSIWYG menu editor with role-based visibility, per-job overrides, clone between roles |
| **Scheduling Cascade** | 3-level override system (Event, Agegroup, Division) for game placement, gaps, and waves |
| **Full Scheduling Pipeline** | Fields, Pairings, Timeslots, Build, QA — all in one place |
| **Referee Assignment** | Assign refs to games with calendar view |
| **Mobile Scorers** | Tablet-friendly real-time scoring interface |
| **Brand / Palette System** | 8 dynamic color palettes with live preview, per-job theming |
| **CADT Tree Filter** | Club-ownership-based filtering (legacy only had flat dropdowns) |
| **Roster Swapper** | Drag-drop player movement between teams |
| **Pool Assignment Tool** | Visual pool assignment for scheduling |
| **Bulk Team Operations** | Transfer all teams between reps, bulk charge entire club |
| **Job Clone** | Clone job settings to a new event |
| **Theme Editor** | Per-job palette and color configuration |
| **Profile Migration** | Move profiles between jobs |
| **Customer Job Revenue** | Revenue reporting tool |
| **Tournament Parking** | Waitlist management tool |
| **Bracket Seeding** | Pre-seed teams for tournament brackets |
| **Rescheduler** | Manual game rescheduling tool |
| **Push Notifications** | Push notification sending to devices |
| **Email Health Monitoring** | Email delivery monitoring |
| **Store with Walk-Up Mode** | E-commerce with kiosk point-of-sale mode |

---

## Quick Summary

- **Fully reproduced:** Player reg core, team reg core, search players, search teams, LADT hierarchy, basic accounting (record payments, discounts, refunds)
- **Gaps to verify:**
  1. Coach management step in team registration (SOCCER flow)
  2. Batch email/SMS from search results
  3. ARB sweep automation (find behind-in-payment, send emails, import transactions)
  4. Family-wide payment distribution (one charge across multiple players)
  5. File uploads during registration (headshots, medical forms, insurance cards)
  6. Bulk "update all player fees to agegroup fees" tool
  7. Admin charges by month/year
  8. Delete registration from search
- **20+ major new features** the legacy never had
