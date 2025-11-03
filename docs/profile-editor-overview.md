# Profile Metadata Editor – v1 Overview (November 3, 2025)

This document summarizes the current state of the Profile Metadata Editor and related migration behavior. It complements the original design docs and the Angular coding standards.

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

- Job Options (overrides) + Sources
  - CRUD for per-job override option sets, mapped to `Jobs.JsonOptions`.
  - Read-only Sources from `Registrations` columns with a “Copy to override” action.
  - Flexible key matching bridges differences like `positions` ↔ `List_Positions` and `ListSizes_Jersey`.

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

## Open follow-ups

- Live preview panel (deferred).
- Modal a11y refinements (consider native <dialog> with focus trapping helpers).
- Extract complex client methods to helpers to reduce cognitive complexity warnings.

## Files of interest

- Frontend
  - `src/app/admin/profile-editor/profile-editor.component.ts` – Signals, DnD, confirm modal helpers, “Add Field” placement selector.
  - `src/app/admin/profile-editor/profile-editor.component.html` – Grouped table, badges, confirm modal, Add Field modal.
  - `src/app/admin/profile-editor/profile-editor.component.scss` – Drag grip and table polish.
  - `src/app/admin/profile-editor/allowed-fields.ts` – Flat, deduped static list for the Add Field picker (one entry per field name).
- Backend
  - `TSIC.API/Services/ProfileMetadataMigrationService.cs` – Normalization in update and migration paths.
  - `TSIC.API/Controllers/ProfileMigrationController.cs` – Admin endpoints.

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
