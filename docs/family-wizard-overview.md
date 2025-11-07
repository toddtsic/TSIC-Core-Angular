# Family Wizard Overview

**Date**: November 7, 2025  
**Status**: Draft — scaffolding the Family-centric flow that precedes Player-specific registration steps.

---

## Purpose

Establish a clear, reusable pattern for flows that start from the Family Account context and then select a specific Family User (child) before entering a job-specific wizard (e.g., Player Registration Wizard).

This document complements:
- `wizard-theme-system.md` (theming)
- `player-registration-architecture.md` (overall registration architecture)
- `player-registration-wizard-flow.md` (routing/auth specifics)

---

## Core Concepts

- Family Account (parent) authenticates and owns the session.
- Family Users (children) are separate `AspNetUsers` linked via `Family_Members`.
- The Family Wizard provides:
  1) A themed entry point for Family login
  2) A selection UI for choosing one or more Family Users
  3) A bridge into the target wizard (player registration), carrying selected user context

---

## Theming & Entry

- Entry is themed with `theme=family` and custom header/subHeader for clarity.
- Entry may be invoked directly or from another wizard's Family Check step.
- Always carry a `returnUrl` back to the originating wizard.

Example entry URL:
```
/tsic/login?theme=family&header=Family%20Account%20Login&subHeader=Sign%20in%20to%20continue&returnUrl=/FALL25/register-player%3Fstep%3Dstart
```

---

## Selection Step (Planned)

UI Requirements:
- List of Family Users (children) with name, grad year, basic demographics.
- Optional filters/search (by name, age/grad year).
- Single-select for PRW (PP type); multi-select for CAC flows.
- Action buttons: Continue, Cancel/Back.

State:
- `activeFamilyUser` (single) or `selectedFamilyUsers[]` (multi) stored in wizard service.

Actions:
- On Continue → route back to originating wizard via `returnUrl` and set selection state.
- If `returnUrl` includes a `jobPath`, subsequent steps can query registration summary for the selected user.

---

## Backend Support (Planned)

Endpoints:
- `GET /api/family/users` — list Family Users for the authenticated Family Account.
- (Optional) `GET /api/family/users/{id}` — detailed view for a specific user.

Models:
```
FamilyUser {
  userId: string,
  displayName: string,
  dob?: string,
  gradYear?: string,
  isDefault?: boolean
}
```

Security:
- Only return children for the authenticated Family Account.
- Consider soft limits (pagination) when family size is large.

---

## Integration with Player Registration

- After selection, PRW step logic checks registration summary for `(jobId, userId)` and either resumes or creates a new registration.
- Token enrichment (`select/{regId}`) occurs only after the PRW locks onto a concrete registration id.

---

## Edge Cases

- No Family Users found → Offer CTA to add a player (future scope) or support out-of-band creation flow.
- Multiple default flags → Resolve during list retrieval (mark exactly one default or none).
- Inconsistent data (missing DOB/grad year) → display gracefully; do not block selection unless required by job.
- Access control mismatch → ensure server verifies parent-child relationship on all dependent API calls.

---

## Implementation Notes

- Keep Family Wizard visually distinct using the family theme; reuse the shared wizard scaffolding (sticky header, toolbar, bottom nav).
- Maintain a single source of truth for the selected Family User in `registration-wizard.service.ts` to simplify downstream steps.
- Normalize all `returnUrl` values to internal, decoded paths before navigation.

---

**Owner**: Family & Registration Experience  
**Last Updated By**: Automated Assistant (GitHub Copilot)
