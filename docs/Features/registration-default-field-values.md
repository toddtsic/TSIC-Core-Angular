# Registration Historical Default Field Values

**Updated**: November 24, 2025
**Scope**: Player Registration Wizard – form prefill logic for new registrations.

## Overview
Unregistered players selected for registration are now prefilled with the most recent value they used for each visible profile field across ANY prior job registration. This improves UX by reducing repetitive seasonal data entry.

Backend (`FamilyService.GetFamilyPlayersAsync`) enriches each unregistered player with a `DefaultFieldValues` dictionary: latest non-null, non-empty value for each visible profile field name (after metadata alias mapping). The frontend maps this to `defaultFieldValues` on `FamilyPlayer`.

## Source of Values
1. Gather all registrations for each child across all jobs.
2. Traverse in descending chronological order.
3. For each visible profile field: first non-null, non-empty value wins.
4. Hidden/admin-only fields excluded; waiver acceptance checkboxes excluded.
5. Values are not mutated; registration snapshots remain immutable.

## Merge Rules (Frontend)
| Rule | Description |
|------|-------------|
| Apply only when | Player is unregistered in current job AND newly selected. |
| Blank check | Field considered blank if `null`, empty string, or empty array. |
| Preserve existing | Do not override values already set from prior registration or manual edits. |
| Alias resolution | Attempt alias map (PascalCase db column → UI schema name) when raw key not found. |
| Eligibility auto-seed | If constraint field blank, seed from defaults after merge. |

## Eligibility Interaction
If job constraint (e.g., `BYGRADYEAR`) is active, the corresponding constraint field (e.g., `gradYear`) is auto-selected when a default exists. Unified `teamConstraintValue` updates when all selected players share the same eligibility after seeding.

## Advisory Nature
Prefilled values are purely convenience defaults:
- User can overwrite immediately.
- No backend write occurs until normal registration submission.
- Does not toggle any registration flags (e.g., `BActive`).

## Non-Goals
| Excluded | Rationale |
|----------|-----------|
| Cross-family value sharing | Privacy and data integrity. |
| Inference for missing DOB/Gender | Critical fields must be explicit; avoid silent assumptions. |
| Hidden/admin-only fields | Avoid exposing internal fields in public flow. |
| Waiver acceptance propagation | Must be re-accepted each job (version change). |

## Data Shape
```json
{
  "playerId": "abc123",
  "registered": false,
  "defaultFieldValues": {
    "gradYear": "2027",
    "clubName": "Lax United",
    "jerseySize": "M",
    "allergies": "Peanuts"
  }
}
```

## Frontend Implementation Hooks
- Extraction: `buildFamilyPlayersList()` in `registration-wizard.service.ts`.
- Seeding: `seedPlayerValuesFromDefaults()` on metadata parse & `togglePlayerSelection()` when player newly selected.
- Eligibility auto-seed: within selection handler if field blank.

## Testing Recommendations
| Scenario | Expected |
|----------|----------|
| Player with prior registrations selects | Blank form controls fill, others remain blank. |
| Player already registered this job | No default merge occurs. |
| Player deselects then re-selects | Merge re-applies only to fields still blank. |
| Field has existing manual edit | Default is not applied (preserved user edit). |
| Eligibility constraint active & default present | Eligibility value auto-selected. |

## Future Enhancements
- Add server-side filtering to exclude stale values (e.g., older than N seasons) if needed.
- Provide UI badge indicating prefilled origin ("From previous registration").
- Allow opt-out toggle per field for auditing.

**Owner**: Player Registration Modernization Initiative
