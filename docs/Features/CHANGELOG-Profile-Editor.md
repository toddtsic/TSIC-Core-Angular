# Profile Metadata Editor – Changelog

All notable changes to the Profile Metadata Editor are documented here.

## 2025-11-03
- Enforced hidden normalization during re-migration and update paths (visibility=hidden → inputType=HIDDEN).
- Server-side ordering normalized to Hidden → Public → Admin; sequential order values.
- Grouped table updated to reflect group order; validation badges added in the list view.
- Public-only drag-and-drop ordering using Angular CDK; Hidden/Admin are fixed.
- Added a clear dotted grip as the drag handle; improved discoverability and removed checkbox-like affordance.
- Replaced browser confirm() with a Bootstrap confirmation modal for destructive actions (Remove field, Delete option set).
- Added `docs/profile-editor-overview.md` capturing v1 feature set and behavior.
 - New: “Add Field” placement selector (Public/Admin Only/Hidden). Static list no longer dictates visibility; Hidden placement forces inputType=HIDDEN.
 - New: `allowed-fields.ts` flattened and deduped (one row per field name; comments removed; alphabetical order).
 - New: Admin endpoint `GET /api/admin/profile-migration/allowed-field-domain` to export a deduped domain from stored metadata (for one-time list refresh).
 - Fix: Domain aggregation `SeenInProfiles` now counts total sightings per field name (not max of a bucket).

## 2025-11-02
- Fixed options mapping across backend/frontend for Job JsonOptions; added flexible key matching (e.g., ListSizes_Jersey).
- UI polish: Display type spacing, field name read-only, disabled actions for hidden fields.

## 2025-11-01
- Initial Profile Editor implementation (fields CRUD, validation testing, clone profile).
