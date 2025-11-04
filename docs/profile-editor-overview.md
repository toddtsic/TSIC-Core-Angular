# Profile Metadata Editor – v1.2 Overview (November 4, 2025)

This document summarizes the current state of the Profile Metadata Editor and related migration behavior. It complements the original design docs and the Angular coding standards.

## What’s new in v1.2

- Accessibility and modal cleanup
  - Converted all editor and migration modals to native HTML <dialog> for better a11y; ESC-to-close supported and autofocus added to primary controls.
  - Removed legacy Bootstrap modal markup/styles; SCSS now targets `.tsic-dialog` with a Bootstrap-like look and backdrop.
- Subtle feedback
  - Added a small global toast system; drag-and-drop option reorders now auto-save and show success/failure toasts.
- Endpoint pruning and UI alignment
  - Deprecated "sources" endpoints removed from the controller; corresponding UI concepts fully eliminated.
- Code hygiene
  - Trimmed stray console noise in editor paths; removed stray role attributes (e.g., `role="group"`) that triggered a11y lint in layout.

## What’s new in v1.1

- Job Options are strictly per-job
  - Option sets live only in `Jobs.JsonOptions` and are never stored inside `PlayerProfileMetadataJson`.
  - The Options tab now lists only the dropdown lists actually referenced by fields in the currently selected profile.
  - Keys are read-only; rows support drag-and-drop reordering with immediate auto-save.
  - The old "Available Sources" view and "Copy to override" flow have been removed from the UI.

- Options visibility is contextual
  - The Job Options tab is shown only when you're editing the active job's profile.
  - If you navigate to a different profile, the editor auto-returns you to the Fields tab.

- Left-side "This Job’s Player Profile" panel
  - Edit Profile Type, Team Constraint (optional, can be empty), and Allow Pay In Full.
  - Apply writes `Job.CoreRegformPlayer` and then refreshes metadata/options in the UI.
  - Reset restores the last applied values.
  - A warning-styled badge displays the raw CoreRegform string for quick verification.
  - Dirty-state protections: unsaved-changes badge, guarded profile switching, and `beforeunload` warning.

- CoreRegform semantics tightened
  - Team Constraint is optional; when empty it's omitted from the pipe-delimited string (no stray pipes).
  - `ALLOWPIF` is appended only when enabled.

- Apply button polish
  - Ghosted/disabled appearance until changes exist; highlighted with a subtle pulse/glow when actionable.

- Backend admin endpoints expanded
  - `GET /api/admin/profile-migration/profiles/current/config` → `{ profileType, teamConstraint, allowPayInFull, coreRegform, metadata }`.
  - `PUT /api/admin/profile-migration/profiles/current/config` → accepts `{ profileType, teamConstraint, allowPayInFull }`, persists `CoreRegformPlayer`, and returns updated `{ ... , coreRegform, metadata }`.

## What’s in v1

- Hidden normalization everywhere
  - Any field with `visibility = hidden` is persisted as `inputType = HIDDEN`.
  - Applied in both editor saves and the re‑migration pipeline.
  - Server renumbers and orders fields in groups: `Hidden → Public → Admin`.

- Grouped fields table
  - Sections: `Hidden` (first), `Public` (draggable), `Admin Only` (third).
  - Public-only drag-and-drop ordering using Angular CDK.
  - Hidden/Admin rows are not draggable; Hidden actions are disabled.
  - Validation column shows concise badges (e.g., `required`, `minLen:3`, `email`).
  - Header includes a warning-colored badge with the active profile type.
  - New Field modal offers a dropdown of allowed fields (unused only) based on a static list in `allowed-fields.ts`.
  - Placement is chosen at add time (“Place in: Public | Admin Only | Hidden”). The static list no longer determines visibility.
  - If you place a field into Hidden, its `inputType` is forced to `HIDDEN` automatically.

- Job Options (per-job overrides only)
  - CRUD for per-job override option sets, mapped to `Jobs.JsonOptions`.
  - View shows only lists used by the active profile; the Sources concept has been removed from the editor.
  - Flexible key matching still bridges differences like `positions` ↔ `List_Positions` and `ListSizes_Jersey`.

- Safer destructive actions
  - Destructive actions use Bootstrap-styled confirmation modals (no browser dialogs).
  - See ANGULAR-CODING-STANDARDS.md → Confirmation Dialogs for the pattern.

- Parser and migration
  - View-first parsing with admin/hidden detection from .cshtml.
  - Options mapping normalization across backend/frontend.
  - Excludes `PP1_Player_Regform` from profile summaries and batches.
  - GitHub contents fetch is configurable by branch (`ref=master2025`) and applied to Unify-2024.

## UX details

- Drag-only in Public, using a clearly visible dotted grip to the left of the Order number.
- Order numbers update immediately after a drop; Save persists changes.
- Field name is read-only; visibility toggle enforces the hidden rule.
- Public row actions: Edit, Test Validation, Remove. Hidden rows are read-only.

## API and server behavior

- UpdateProfileMetadataAsync
  - Normalizes hidden input types; orders fields `Hidden → Public → Admin`; updates all jobs using the profile.
- MigrateJobAsync / MigrateProfileAsync
  - Apply identical normalization as the editor update path, ensuring re-migration is consistent with saved edits.
- TestFieldValidation
  - Returns granular messages for required, min/max, pattern, email, and numeric range checks.

- Current Job Profile Config
  - `GET /api/admin/profile-migration/profiles/current/config` returns the active job’s profile config plus refreshed metadata.
  - `PUT /api/admin/profile-migration/profiles/current/config` updates `CoreRegformPlayer` with optional Team Constraint and `ALLOWPIF`, then re-materializes `PlayerProfileMetadataJson`.

## Open follow-ups

- Live preview panel (deferred).
- Dialog focus trap utility (beyond autofocus) for full keyboard loop containment.
- Extract complex client methods further to reduce cognitive complexity in a few remaining areas.

## Files of interest

- Frontend
  - `src/app/admin/profile-editor/profile-editor.component.ts` – Signals, DnD, confirm modal helpers, “Add Field” placement selector.
  - `src/app/admin/profile-editor/profile-editor.component.html` – Grouped table, badges, confirm modal, Add Field modal.
  - `src/app/admin/profile-editor/profile-editor.component.scss` – Drag grip and table polish.
  - `src/app/admin/profile-editor/allowed-fields.ts` – Flat, deduped static list for the Add Field picker (one entry per field name).
  - `src/app/core/services/profile-migration.service.ts` – Client for current job profile config (GET/PUT) and migration-related APIs.
- Backend
  - `TSIC.API/Services/ProfileMetadataMigrationService.cs` – Normalization in update and migration paths.
  - `TSIC.API/Controllers/ProfileMigrationController.cs` – Admin endpoints (includes current job profile config GET/PUT).

## Standards reminder

- Use Bootstrap confirmation modals for destructive actions; avoid browser dialogs.
- Restrict DnD to Public; server renormalizes and renumbers on save.
- Maintain hidden → `HIDDEN` invariant across the stack.
- Any field can be placed into any visibility (Public/Admin Only/Hidden) when adding; Hidden forces `inputType=HIDDEN`.

## Allowed fields domain and export

- The Add Field list is a flat, deduped array defined in `allowed-fields.ts` (one entry per field `name`).
- The list’s `displayName` and `inputType` act as defaults; visibility is chosen during add and is not taken from this list.
- Admin endpoint for one-time export from stored metadata:
  - `GET /api/admin/profile-migration/allowed-field-domain`
  - Returns `{ name, displayName, defaultInputType, defaultVisibility, seenInProfiles }[]` aggregated across all `Jobs.PlayerProfileMetadataJson`.
  - Useful for generating or refreshing the static list.
