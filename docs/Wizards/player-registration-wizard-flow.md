# Player Registration Wizard Flow (Interim Spec)

**Date**: November 7, 2025 (original) / Updated: November 24, 2025  
**Status**: Active — core flow implemented; recent enhancements (defaults, waivers isolation, payment refactor, auto confirmation email) appended below.

---

## 1. Goals

Provide a deterministic, wizard-driven player registration experience for Family Accounts:
- After Family login, return directly to the Player Registration Wizard (PRW) without detouring to role selection.
- Let the wizard, not the login page, determine which Family User (child) and which registration record (existing vs new) to use.
- Keep authentication minimal at login (Phase 1 token); enrich only after registration context is established.
- Support theming and contextual headers for login and wizard screens.

---

## 2. Current Routing & Auth Behavior

### Deep Link Pattern
```
/{jobPath}/register-player?step=start
/register-player?step=start            (no jobPath context)
```
`FamilyCheck` constructs a normalized `returnUrl` for the login screen:
```
/tsic/login?theme=family&header=Family%20Account%20Login&subHeader=Sign%20in%20to%20continue&intent=player-register&returnUrl=/{jobPath}/register-player%3Fstep%3Dstart
```

### Guards
- `redirectAuthenticatedGuard`: If already authenticated and `returnUrl` is present and internal, navigate to it immediately (bypasses role-selection).
- Other guards (auth/role/job) remain unchanged but now respect the wizard deep link contract.

### Wizard Initialization
- `PlayerRegistrationWizardComponent.ngOnInit()` calls `auth.logoutLocal()` intentionally to force a clean state, expecting a redirect through the login path with the preserved `returnUrl`.
- After re-authentication, the guard returns the user to the wizard.

### Login Normalization
- `LoginComponent` repairs malformed `returnUrl` (decodes, removes leading `//`) and navigates to it; does NOT auto-select or enrich token.

### Fetch De-Duplication
- `AuthService` maintains `_registrationsFetched` flag; resets on `logoutLocal()` to avoid repeated `/registrations` calls after login.

### UX Enhancement
- Wizard header now shows either the active Family User (once selected) or the Family Account username (from `last_username` in local storage) as a badge.

---

## 3. Upcoming Wizard Steps (Incremental Plan)

1. Family User Selection (NEW)
   - List available child accounts (Family Users) associated with the logged-in Family Account.
   - On selection: set `activeFamilyUser` signal.
   - Determine job context (`jobPath`) and proceed to registration summary check.

2. Existing Registration Detection
   - Call backend summary endpoint: does a registration exist for (jobId, childUserId)?
   - Branch:
     - Existing complete registration → load summary / allow edit or proceed to confirmation.
     - In-progress (partial) → resume form with stored data.
     - None → create new registration (Phase 2 token enrichment after creation).

3. Dynamic Form Assembly
   - Fetch `PlayerProfileMetadataJson` and `JsonOptions` (already outlined in architecture doc).
   - Build reactive form from metadata; prefill from existing registration (or defaults for new).

4. Team / Constraint Handling
   - If the job profile specifies a constraint (BYGRADYEAR, etc.), gather constraint value first and show the Teams tab.
   - Teams step: robust UI feedback for full teams (strike-through, faded, disabled, tooltip/message).
   - If no constraint, skip Teams tab and add a TeamId field to each player form in the Forms step, with the same full/disabled logic.
   - Selection is prevented for full teams in both steps.

5. Pre-Payment Roster Check & Pending Registration
   - Before payment, the wizard calls the backend `preSubmit` API to check roster capacity and create pending registrations (BActive=false).
   - Only after successful payment are registrations activated.

6. Review & Payment
   - Summarize chosen teams, fees, profile fields.
   - Offer Pay-In-Full vs Deposit (if `allowPayInFull`).

7. Confirmation & Token Enrichment
   - On successful submission, enrich token with `regId`, `jobPath` (Phase 2 token) for downstream guarded routes.

8. Auto-Fill on Restart
   - Restarting registration auto-fills team selections and form data from saved work, using only fields defined in the current profile metadata.

---

## 4. Data & Signals (Frontend Store Additions)

```
activeFamilyUser: signal<FamilyUser | null>
registrationSummary: signal<RegistrationSummary | null>
profileMetadata: signal<ProfileMetadata | null>
playerForm: signal<FormGroup | null>
teamConstraintValue: signal<string | null>
selectedTeam: signal<Team | null>                // PP type
selectedCamps: signal<Team[]>                    // CAC type
paymentChoice: signal<'PIF' | 'Deposit'>
submissionState: signal<'idle' | 'submitting' | 'success' | 'error'>
```

Edge cases flagged for future handling:
- Multiple existing registrations for same child/job (should not occur; detect & surface error).
- Roster full / waitlist state triggers alternative message or disables submission.
- USLax number invalid or expiring before `USLaxNumberValidThroughDate`.
- Profile migration differences (old vs new metadata present).

---

## 5. Backend Endpoints (Planned)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/family/users` | GET | List child Family Users for authenticated parent |
| `/api/registration/summary?jobPath={jobPath}&childUserId={id}` | GET | Detect existing registration & status |
| `/api/registration/profile/{jobPath}` | GET | Fetch profile metadata + options (already spec'd) |
| `/api/registration/create` | POST | Create new registration (returns `regId`) |
| `/api/registration/update/{regId}` | PUT | Save partial/in-progress form data |
| `/api/registration/select/{regId}` | POST | Enrich token (Phase 2) with registration context |
| `/api/registration/teams` | GET | Filter teams/camps based on constraint |
| `/api/registration/validate-uslax` | POST | External USLax number validation |

Token enrichment is intentionally decoupled: only executed after `regId` is known and context locked.

---

## 6. ReturnUrl & Intent Contract

| Parameter | Source | Description |
|-----------|--------|-------------|
| `returnUrl` | FamilyCheck / deep link | Path wizard should resume after login |
| `intent=player-register` | FamilyCheck | Advisory; wizard drives context, login ignores for selection |
| `theme` | Theming system | Colors + header gradient (e.g., `family`, `player`) |
| `header`, `subHeader` | Theming system | Text labels for login UI |

Normalization Rules:
- Decode URL-encoded `returnUrl`.
- Strip leading `//` to prevent router misparse.
- Ensure internal path (no protocol/host); if external, fallback to safe default (role selection or home).

---

## 7. Open Items / Next Steps

| Item | Status | Notes |
|------|--------|-------|
| Family user list retrieval | Partial | Basic family players endpoint supplies player set; dedicated user list deferred |
| Registration summary check | Deferred | Current flow infers existing vs new from `registered` flag in players payload |
| Token enrichment timing | In Progress | Phase 2 enrichment occurs post successful payment (confirmation load) |
| Dynamic form prefill | Implemented | Historical latest registration snapshot + new `defaultFieldValues` for unregistered players |
| Team filtering integration | Implemented | Constraint → team gating; full-team UI states present |
| USLax async validation | Implemented | Field detection + status signals + validation messages |
| Edge case surfaces (waitlist/full) | Partial | Full teams disabled; waitlist messaging TBD |

---

## 8. Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Multiple fetches of registration summary | Cache per (jobPath, childUserId) until invalidated |
| Race condition between logoutLocal and guard redirects | Ensure logout happens only on wizard init; guard uses `returnUrl` post-login |
| Token enrichment too early | Restrict enrichment call to explicit selection/creation step completion |
| Metadata absent for legacy jobs | Fallback UI: show message and block dynamic form, link to legacy flow |
| Inconsistent `jobPath` derivation | Centralize derivation logic in a utility used by FamilyCheck and summary service |

---

## 9. Traceability to Architecture Doc

This flow spec links to architectural elements defined in `player-registration-architecture.md`:
- Metadata system → Profile fields and dynamic form generation.
- Team filtering → Phase 2 dynamic team selection.
- USLax validation → Phase 3 external validation integration.
- Multi-step wizard → Proposed 6–7 step layout (this spec starts with minimal subset and grows).

---

## 10. Implementation Increment Checklist

Initial increment (focused on deterministic return + user selection):
- [x] Normalize returnUrl and theming on login
- [x] Guard respects internal returnUrl
- [x] Wizard header shows context badges
- [ ] Family user selection step
- [ ] Registration summary fetch logic
- [ ] Create-or-resume branching
- [ ] Token enrichment after selection
### Post-Nov 20 Enhancements (Current)
- [x] Dedicated Waivers step (removed waiver checkboxes from Forms)
- [x] Historical field defaults (`defaultFieldValues`) merged into blank form controls for newly selected, unregistered players
- [x] Eligibility auto-seed from defaults (when constraint active)
- [x] Payment step refactor (see `payment-refactor-summary.md`)
- [x] Automatic confirmation email on every successful payment (CC or ARB) with advisory `BConfirmationSent` flag update
- [x] Re-send confirmation email endpoint + UI button
- [x] Finish button navigates back to job home (`/{jobPath}`) instead of cycling wizard
- [ ] Waitlist UI/flag integration
- [ ] Registration summary consolidation endpoint (single source for status)
- [ ] Multi-player batch eligibility optimization (current per-player pass is adequate at scale < 200)

---

## 11. Historical Field Defaults & Prefill (New)
Unregistered players now receive per-field defaults derived from the most recent value across ANY prior registration (job-agnostic). Backend supplies a `defaultFieldValues` dictionary (visible-only fields). Frontend merge rules:
1. Apply only when player is newly selected and not yet registered in current job.
2. Only fill controls that are blank (`null`, empty string, or empty array).
3. Preserve values seeded from prior registration snapshot if player is already registered.
4. Honor alias field mapping (PascalCase db columns → UI schema names).
5. Eligibility constraint value auto-seeded if corresponding field is defaulted.

Benefits:
- Reduces repetitive data entry across seasonal jobs.
- Maintains immutability of historical registrations (no retroactive writes).
- Keeps advisory nature: user can overwrite any prefilled value immediately.

## 12. Confirmation Email Automation (New)
Each successful payment (one-time charge or ARB subscription create) triggers an immediate confirmation email send. Design details:
- No suppression: email always sent; existing `BConfirmationSent` flags only updated from false → true.
- Environment short-circuit: in Development, send is skipped unless `sendInDevelopment=true` passed (see `EmailService.SendAsync`).
- Resend endpoint allows manual re-send without affecting original send semantics.
- Token substitution service now normalizes From header brand formatting.

## 13. Finish Navigation Behavior (New)
The Finish button on the Confirmation step now exits the wizard and navigates directly to the job home (`/{jobPath}`) for a clean post-registration return path.

## 14. Future Documentation Targets
- Consolidated "Registration Context" doc (will merge remaining pending items: family user explicit selection, summary endpoint schema).
- Waitlist lifecycle & UI semantics.
- Performance notes for large family rosters (signal update batching).

**Last Updated By**: GitHub Copilot (Automated Assistant)

Subsequent increments add form metadata rendering, team filtering, payment, USLax validation.

---

**Document Owner**: Player Registration Modernization Initiative  
**Last Updated By**: Automated Assistant (GitHub Copilot)
