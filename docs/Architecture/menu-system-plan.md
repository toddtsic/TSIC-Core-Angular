# TSIC Menu System: Migration, Caching, and Angular Integration (Draft)

Status: deferred (important) — captured to resume after Player Wizard work.

## Goals
- Preserve legacy menu data and behavior while we migrate to Angular.
- Avoid modifying current production tables; introduce V2 tables for the new app.
- Separate role-independent job metadata from role-dependent menus for better caching and UX.

---

## Legacy schema (read-only)

Tables per ERD:
- JobMenus (Jobs)
  - Keys: menuId (PK), jobId (FK → Jobs), roleId (FK → AspNetRoles, nullable ⇒ anonymous menu)
  - Flags/metadata: active, menuTypeId, modified, tag
- JobMenu_Items (Jobs)
  - Keys: menuItemId (PK), menuId (FK → JobMenus), parentMenuItemId (nullable)
  - Critical fields
    - index (ordering)
    - Text (display)
    - Controller, Action (legacy MVC semantics)
    - NavigateUrl (external link override)
    - Target (link target), iconName, bCollapsed, bTextWrap

Hierarchy:
- Two levels supported
  - Root items: parentMenuItemId IS NULL
  - Children: parentMenuItemId = root.menuItemId

Semantics:
- roleId NULL ⇒ anonymous menu (fallback when role-specific not available)

---

## API design

1) GET /jobs/{jobPath}/metadata
- Role-independent metadata (logo, name, banner, static settings)
- Caching: public, strong ETag (metaVersion), Last-Modified
- Cache-Control: `public, max-age=300, stale-while-revalidate=1800`

2) GET /jobs/{jobPath}/menus
- Returns the best-fit menu for current role:
  - Prefer role-specific JobMenus (active) → else anonymous (roleId NULL) → else 404
- Query overrides for diagnostics: `?roleId=...` or `?role=Director`, optional `menuTypeId` or `tag`
- DTO (tree):
```json
{
  "jobId": "...",
  "jobPath": "...",
  "roleId": "GUID|null",
  "roleKey": "anonymous|Parent|Director|ClubRep|...",
  "version": "etagSource",
  "items": [ { /* MenuItemDto (sorted, children grouped) */ } ]
}
```
MenuItemDto fields:
- id, parentId, index, text, iconName, collapsed, textWrap
- routerLink (string|string[]), navigateUrl (string), target ("_blank"|"_self"|...)
- children[]

Caching:
- Anonymous: `public, max-age=300, stale-while-revalidate=1800`
- Role menus: `private, max-age=60, stale-while-revalidate=300`
- ETag: hash(menuId + JobMenus.modified + all JobMenu_Items.modified + item count + checksum(Text/urls))
- 304 support via If-None-Match

Server cache:
- IMemoryCache / Redis key: `menu:{jobId}:{menuTypeId}:{tag}:{roleKey}`
- TTL: 2–5 minutes (evict on admin save in the future editor)

---

## Controller/Action → Angular routes

Link precedence per item:
1) `NavigateUrl` present ⇒ use as href (external/absolute). Respect `Target`.
2) `Controller` + `Action` ⇒ build `routerLink` under `/:jobPath` with a mapping table (extensible):
   - { Registration, Player } → `/:jobPath/register-player`
   - { Home, Index } → `/:jobPath`
   - { Forms, Index } → `/:jobPath/forms`
   - Default fallback: `/:jobPath/{controller-lower}/{action-lower}`
3) Reports (e.g., `ReportName`, `ReportExportTypeID`) can map to `/:jobPath/reports/:reportName` or a download endpoint (to be defined when report UI lands).

---

## Angular client (SWR caching)

Keys:
- Metadata: `jobMeta:{jobPath}`
- Menus: `jobMenu:{jobPath}:anonymous` and `jobMenu:{jobPath}:{role}`

Behavior:
- Serve cached immediately if present; background revalidate with If-None-Match.
- TTLs: metadata 15–30 min (SWR every ~5 min), menu 2–5 min (SWR every ~1 min or on focus/role change).
- Persistence: localStorage only for metadata and anonymous menu; role menus in-memory (or minimized persistence).

MenuService (planned):
- `getMenu(jobPath, roleKey|'anonymous')` → signal/observable of MenuVM
- Produces sorted roots with grouped children; normalizes links per the mapping rules.

---

## Migration plan (no changes to legacy tables)

- Create new tables: `JobMenuV2`, `JobMenuItemV2`
  - Include: role key (nullable for anonymous), menu type/tag, ordering, icon, link model, visibility predicates, audit (created/modified), `version`/`rowversion`
- Migration interface (read-only on legacy):
  - Load legacy menu tree for a job+role or anonymous
  - Allow mapping preview and per-item adjustments (controller/action → routerLink mapping hints)
  - Export into V2 tables (one-time or incremental)
- Editor interface (for V2):
  - CRUD items, drag/drop reorder, add children, edit link types, assign roles, toggle active
  - Version bump on save; invalidate server cache for the affected job/role

---

## Versioning and invalidation

- Metadata: `metaVersion` from Jobs/JobDisplayOptions (rowversion/hash of updated timestamps)
- Menus: `menuVersion` from JobMenus/JobMenu_Items modified fields + checksum of content
- ETag = version string; return 304 when unchanged
- On admin updates (future editor): evict specific cache keys; clients revalidate via ETag

---

## Open items / next steps (deferred)

- Define concrete mapping table for all legacy Controller/Action pairs currently in use
- Implement API endpoints with ETag/304 and IMemoryCache
- Build `MenuService` (Angular) with SWR and signals
- Design and scaffold V2 schema + migration/export UI + editor UI
- Wire role resolution and anonymous fallback in the endpoint

---

## Why separate metadata and menus?

- Better cache efficiency (metadata changes rarely; menus vary by role and usage)
- Faster first paint (metadata available to anonymous users without auth)
- Clearer invalidation and versioning boundaries

---

Owner notes:
- This document captures the agreed design direction and shall be revisited after Player Wizard milestones.
- See backlog todos: #49–#52 (migration/editor/caching), #33–#40 (caching impl details).