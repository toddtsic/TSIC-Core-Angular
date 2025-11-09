# Waivers Step Implementation (November 2025)

This document describes the dedicated Waivers & Agreements step recently added to the Player Registration Wizard. It replaces legacy inline waiver checkboxes formerly rendered inside the player forms step.

## Goals
- Centralize display and acceptance of waiver/policy HTML content.
- Provide a consistent UX: review each waiver, explicitly accept required ones.
- Support read-only (edit / already-registered) flows with clear locked status.
- Eliminate duplicated / scattered waiver logic across form schemas.
- Normalize backend field casing differences (PascalCase vs camelCase).

## Sources of Waiver Content
Waiver and policy HTML blocks are delivered in `GET /api/jobs/{jobPath}` response. The client reads them case-insensitively:

```
PlayerRegReleaseOfLiability
PlayerRegCodeOfConduct
PlayerRegCovid19Waiver
PlayerRegRefundPolicy
```

Each non-empty block becomes a waiver definition. Refund Policy is treated identically to other waivers.

## Frontend Components & State

| File | Purpose |
|------|---------|
| `registration-wizard.service.ts` | Loads job metadata, extracts waiver HTML, builds definitions, tracks acceptance (signals). |
| `steps/waivers.component.ts` | Renders accordion UI, enforces acceptance, shows locked badges. |
| `steps/player-forms.component.ts` | (Updated) Legacy waiver section removed. |

### Service Additions
- `waiverDefinitions()` signal: ordered array of `{ id, title, html, required, version }` (version currently internal only, not displayed).
- `jobWaivers()` (raw object) retained for debugging / backward compatibility.
- Case-insensitive metadata extraction helper ensures both `PlayerRegRefundPolicy` and `playerRegRefundPolicy` styles work.
- `seedAcceptedWaiversIfReadOnly()` auto-accepts required waivers when:
  - Start mode is `edit`, or
  - Any selected player is already registered for this job AND user has not manually toggled acceptance.

### Acceptance Logic
- Required waivers gate navigation (Continue disabled until all required accepted).
- Read-only/edit mode disables checkboxes and applies locked styling.
- Acceptance state persists as the user navigates back/forth in the wizard (stored in service signals).

### Read-Only Detection
A waiver is locked (checkbox disabled) when either:
1. Wizard `startMode === 'edit'`, or
2. Any selected player appears in `familyPlayers()` with `registered === true`.

### UI / UX Details
- Single-open accordion (exclusive) using lightweight state, not Bootstrap JS.
- First waiver auto-expanded on `ngAfterViewInit`.
- Header badges:
  - Green/Red Accepted / Not Accepted status badge.
  - Secondary gray Locked badge appears immediately after Accepted when read-only.
- Body checkbox section duplicates the Locked badge next to the label for localized clarity.
- Missing HTML displays subtle neutral placeholder: "No content provided for this waiver." (useful during backend population).
- Removed legacy signature capture block (no general signature required; per-waiver acknowledgment only).

### Accessibility Considerations
- `aria-expanded` bound on header button.
- `role="region"` plus `aria-labelledby` on collapse content container.
- Visual badges accompanied by textual status (screen readers announce Accepted / Not Accepted / Locked).

## Data Model (Client)
```ts
interface WaiverDefinition {
  id: string;        // e.g. 'ReleaseOfLiability'
  title: string;     // Human-friendly title derived from property name
  html: string;      // Raw HTML content from job metadata
  required: boolean; // Currently all recognized waivers treated as required (extensible)
  version?: number;  // Reserved for future versioning (not shown)
}
```

## Extensibility Hooks
| Area | Possible Future Enhancement |
|------|-----------------------------|
| Per-player tracking | Store which player accepted if multi-signer flows are introduced. |
| Versioning | Display waiver version + acceptance timestamp if backend supplies it. |
| Optional waivers | Support `required: false` to allow optional acknowledgments. |
| E-sign | Reintroduce signature capture per waiver if legal requirement emerges. |
| Audit trail | Persist acceptance metadata (who, when, IP) upon submission step. |

## Migration Notes
- Legacy profile checkbox fields like `BWaiverSigned1` remain in profile metadata for backward compatibility but are suppressed from the Player Forms step (filtered by `waiverFieldNames()` in service logic).
- Any reporting or export process that previously relied on `BWaiverSigned1` should be updated to consult new acceptance mapping once submission endpoint evolves to persist acceptance events.

## Known Constraints
- Acceptance currently not persisted server-side until full registration submission feature is implemented (future payment/submission sprint).
- All waiver blocks assumed required; optional classification not yet part of backend contract.
- No diff/updated notification if waiver HTML changes mid-session (would require checksum & watcher pattern).

## Testing Checklist
| Scenario | Expected Result |
|----------|-----------------|
| No waiver fields present | Info alert: "No waivers are required..." Continue enabled. |
| Waivers present, new registration | First panel open, others collapsed. Checkboxes enabled. Continue disabled until all required accepted. |
| Editing existing registration | All required waivers show Accepted + Locked; checkboxes disabled; Continue enabled. |
| Mixed players (some previously registered) | Auto-seeded acceptance behaves same as edit mode (read-only). |
| Missing HTML for a waiver | Placeholder alert appears; does not block acceptance. |
| Toggling acceptance | Status badge updates immediately; Continue gating updates. |

## Future Submission Integration
When payment + submission are implemented:
1. Serialize accepted waiver IDs (and optionally timestamps) into the registration payload.
2. Backend stores acceptance records (e.g., `RegistrationWaiverAcceptances` table) with foreign key to registration.
3. On edit flow load: preload acceptance state from stored records instead of auto-seeding purely heuristically.

Suggested backend acceptance table (draft):
```sql
CREATE TABLE RegistrationWaiverAcceptances (
  Id UNIQUEIDENTIFIER PRIMARY KEY,
  RegistrationId UNIQUEIDENTIFIER NOT NULL,
  WaiverKey NVARCHAR(100) NOT NULL,      -- e.g. ReleaseOfLiability
  AcceptedUtc DATETIME2 NOT NULL,
  AcceptedByUserId UNIQUEIDENTIFIER NOT NULL,
  WaiverContentHash CHAR(64) NULL,       -- SHA-256 of HTML at time of acceptance
  CONSTRAINT FK_RWA_Registration FOREIGN KEY (RegistrationId) REFERENCES Registrations(RegistrationId)
);
CREATE INDEX IX_RWA_Registration_WaiverKey ON RegistrationWaiverAcceptances(RegistrationId, WaiverKey);
```

## Summary
The Waivers step centralizes policy review and acceptance with a cleaner, auditable, and extensible design foundation. Legacy per-form waiver checkbox artifacts are deprecated in the UI, reducing duplication and improving clarity for registrants.

*Last updated: 2025-11-09*
