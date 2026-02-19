# Widget Component Taxonomy & Directory Structure

> **Status**: Approved (v4 — link-tile removed, UserWidget layer added)
> **Date**: 2026-02-19
> **Applies to**: All widget components rendered by the Widget Dashboard

---

## Core Concepts

### Two Independent Axes

**Axis 1 — Domain Function** (what is this widget *about*?)
Maps to `WidgetCategory.Workspace` in the database. Determines where code lives.

**Axis 2 — Rendering Pattern** (what *shape* does it take?)
Maps to `Widget.WidgetType` in the database. Determines dashboard layout behavior.

**Visual Variant** (how does it *look* within that shape?)
Carried in `Config` JSON as `displayStyle`. Overridable per job/role/user.

These are independent. A registration widget could be a chart-tile or a status-tile.
A chart-tile shell could render registration data or financial data.

---

## WidgetType (Locked — 3 Values)

| WidgetType | Shape | Description |
|---|---|---|
| `content` | Full-width inline block | Renders custom component inline, no tile chrome |
| `chart-tile` | Bounded grid tile | Data visualization with click-to-expand interaction |
| `status-tile` | Bounded grid tile | Single metric/KPI with trend indicator |

### Consolidation History

| Old Value | New Value | Reason |
|---|---|---|
| `link-tile` | *(removed)* | Navigation now handled by nav menu system; doorbell pattern retired |
| `quick-action` | *(removed)* | Was absorbed into link-tile, then removed with it |
| `workflow-pipeline` | *(removed)* | Was absorbed into link-tile, then removed with it |
| `link-group` | *(removed)* | Was absorbed into link-tile, then removed with it |
| `status-card` | `status-tile` | Renamed for `-tile` consistency |
| `chart` | `chart-tile` | Renamed — it IS a tile first, chart is what's inside |
| `content` | `content` | Unchanged — genuinely NOT a tile |

---

## Workspaces (Locked — 2 Values)

| Workspace | Purpose |
|---|---|
| `public` | Anonymous-facing widgets (banners, bulletins, event contact) |
| `dashboard` | Authenticated user dashboard (charts, KPIs, bulletins for non-admin) |

Spoke workspaces (`player-reg`, `team-reg`, `financial`, `scheduling-pipeline`,
`ladt-tools`, `stores`) were removed — navigation is now handled by the nav menu system.

---

## displayStyle (Config JSON)

The `displayStyle` property in Config JSON controls visual variant within each
WidgetType. When absent, defaults to `standard`. Overridable at four levels:
`Widget.DefaultConfig` → `WidgetDefault.Config` → `JobWidget.Config` → `UserWidget.Config`.

### How displayStyle is rendered

The dashboard host reads `displayStyle` from Config JSON via `getDisplayStyle(widget)`
(returns `'standard'` when absent) and binds a CSS class `ds-{style}` on each widget
container.

**CSS class convention:**
```
ds-{displayStyle} — visual variant
```

Example rendered classes: `chart-tile-wrapper ds-wide`.

**CSS-only vs component logic:**
- **chart-tile** `wide` — pure CSS (`grid-column: span 2`)
- **chart-tile** `spark` — will need component logic (different chart.js config, no expand)
- **status-tile** variants — will need component logic when tiles are activated
- **content** variants — already handled by `componentKey` dispatch; `displayStyle` is metadata

**Implementation files:**
- `widget-dashboard.component.ts` — `WidgetConfig.displayStyle` + `getDisplayStyle()` helper
- `widget-dashboard.component.html` — `ds-{style}` class binding on chart-tile-wrappers
- `widget-dashboard.component.scss` — `.ds-wide` CSS rules
- `widget-editor.component.ts` — `displayStyleOptions` map for editor UI

### content

| displayStyle | Rendering | Implementation |
|---|---|---|
| `banner` | Hero image/logo block (client-banner) | Dispatch by componentKey |
| `feed` | Scrollable item list (bulletins, announcements) | Dispatch by componentKey |
| `block` | Static text/HTML panel (future rich-text content) | Future |

### chart-tile

| displayStyle | Rendering | Implementation |
|---|---|---|
| `standard` | Default tile with click-to-expand modal | CSS (no overrides — default look) |
| `wide` | Spans two grid columns (complex visualizations) | CSS (`grid-column: span 2`) |
| `spark` | Compact inline sparkline, no expand | Future (needs component logic) |

### status-tile

| displayStyle | Rendering | Implementation |
|---|---|---|
| `standard` | Icon + metric + label + trend indicator | Future (tiles dormant) |
| `hero` | Large featured metric, prominent placement | Future (tiles dormant) |
| `compact` | Number + label only, denser grid | Future (tiles dormant) |

---

## Directory Structure

```
src/app/widgets/
├── widget-registry.ts               ← Central componentKey → Component map
│
├── services/
│   └── widget-dashboard.service.ts  ← Data fetching for widget components
│
├── registration/                    ← Domain: player/team registration
│   ├── player-trend-widget/         ← Uses chart-tile shell
│   ├── team-trend-widget/           ← Uses chart-tile shell (self-guards isTeamJob)
│   └── agegroup-distribution-widget/
│
├── scheduling/                      ← Domain: scheduling & events
│   └── year-over-year-widget/
│
├── event-info/                      ← Domain: event identity & contact
│   └── event-contact-widget/        ← Self-resolves public vs authenticated
│
├── communications/                  ← Domain: bulletins & announcements
│   └── bulletins.component.ts       ← Self-sufficient (injects JobService)
│
├── layout/                          ← Domain: per-job configurable chrome
│   └── client-banner/
│
└── finance/                         ← Domain: financial dashboards (future)
```

### tsconfig path alias

```json
"@widgets/*": ["src/app/widgets/*"]
```

### What stays in `views/home/widget-dashboard/`

- The **host shell** component (hub/public mode switching, grid layout,
  dynamic `NgComponentOutlet` rendering via `WIDGET_REGISTRY`)
- Route configuration

The host **imports from `@widgets/`** — it does not contain widget implementations.

### What stays in `layouts/components/`

Platform-level chrome consistent across ALL jobs (NOT widgets):
- `client-header-bar/` — app header with auth, palette, theme
- `client-footer-bar/` — app footer with copyright

---

## How This Aligns With the Database

```
DB                          Code
──────────────────────────  ──────────────────────────
WidgetCategory.Workspace    → widgets/{domain}/        (directory)
Widget.WidgetType           → tile shape / layout hint (content|chart-tile|status-tile)
Widget.ComponentKey         → WIDGET_REGISTRY lookup   (dispatch key)
Widget.DefaultConfig        → JSON props to component  (displayStyle)
WidgetDefault.Config        → per-role override         (displayStyle, etc.)
JobWidget.Config            → per-job override          (displayStyle, etc.)
UserWidget.Config           → per-user override         (displayStyle, etc.)
UserWidget.IsHidden         → user hides widget from their dashboard
UserWidget.DisplayOrder     → user reorders widgets
```

### Three-Layer Merge

The dashboard assembles widgets by merging three layers:

1. **WidgetDefault** — platform defaults per Role + JobType
2. **JobWidget** — admin per-job overrides (enable/disable, reorder, config)
3. **UserWidget** — per-user customizations (hide, reorder, config)

Each layer can override `DisplayOrder`, `Config`, and `CategoryId`.
UserWidget adds `IsHidden` for user-level visibility control.

When adding a new widget:

1. **Identify its domain** → create/find directory under `widgets/`
2. **Pick its WidgetType** → `content`, `chart-tile`, or `status-tile`
3. **Pick displayStyle** → set in `DefaultConfig` JSON (defaults to `standard` if omitted)
4. **Register in DB** → INSERT into `widgets.Widget` with ComponentKey
5. **Add to `WIDGET_MANIFEST`** → one import + one entry in `widgets/widget-registry.ts`

**Zero changes to the dashboard component.** The registry + `NgComponentOutlet` handles dispatch.

---

## Rules

1. **Organize by domain, not by rendering pattern.** A "Registration Revenue Chart"
   goes in `widgets/registration/`, not `widgets/charts/`.

2. **Rendering shells are generic.** `chart-tile` knows nothing about registration
   or scheduling — it takes data inputs and renders a chart. Domain logic stays
   in the widget component that wraps the shell.

3. **`WidgetType` is a tile shape, not an identity.** It tells the dashboard
   host how to size/layout the widget. It does NOT determine where the code lives.

4. **`displayStyle` is a visual variant, not a type.** Visual differences live
   in Config JSON, not in WidgetType.

5. **Domain directories mirror workspaces.** If a new workspace is added to
   `WidgetCategory`, a corresponding directory appears under `widgets/`.

6. **The host shell is a thin dispatcher.** `widget-dashboard.component.ts` uses
   `WIDGET_REGISTRY` + `NgComponentOutlet` to dynamically render widgets by
   `componentKey`. All rendering logic lives in the widget components themselves.
   Widgets must be **self-sufficient** (inject services, not receive inputs).

7. **Three WidgetTypes only.** Do not add new WidgetType values without explicit
   architectural review. Visual variations belong in `displayStyle`.

---

## Widget Configuration Deployment

### Strategy

Widget configuration lives in five database tables (`widgets.WidgetCategory`,
`widgets.Widget`, `widgets.WidgetDefault`, `widgets.JobWidget`, `widgets.UserWidget`).
The workflow is:

1. **Configure in dev** using the Widget Editor UI (`/:jobPath/admin/widget-editor`)
2. **Export dev config** by running `Export-WidgetConfig.ps1`
3. **Deploy to target** by running the generated SQL against prod (or restored backup)

### Why this approach

- **Prod backups are frequently restored to dev** — widget tables get wiped each time
- The hand-maintained seed script (`seed-widget-dashboard.sql`) is for development
  iteration only; it should not be the source of truth for production
- The export script snapshots the **actual** dev DB state, including any configuration
  done through the Widget Editor UI

### Scripts

| Script | Purpose |
|---|---|
| `scripts/seed-widget-dashboard.sql` | Bootstraps widget tables + sample data for fresh dev DBs |
| `scripts/Export-WidgetConfig.ps1` | Reads dev DB, generates `deploy-widget-config.sql` |
| `scripts/deploy-widget-config.sql` | (generated) Idempotent SQL to deploy widget config to any target DB |
| `scripts/migrate-dashboard-evolution.sql` | One-time migration: removes link-tiles/spokes, creates UserWidget table |

### Export-WidgetConfig.ps1 usage

```powershell
# Default: reads connection from appsettings.Development.json
.\scripts\Export-WidgetConfig.ps1

# Explicit connection string
.\scripts\Export-WidgetConfig.ps1 -ConnectionString "Server=...;Database=...;..."

# Custom output path
.\scripts\Export-WidgetConfig.ps1 -OutputPath ".\scripts\my-export.sql"
```

The generated SQL is fully idempotent:
- Creates `widgets` schema and tables if they don't exist
- Handles `Section` → `Workspace` migration for older prod backups
- Clears and reseeds all four widget tables with `IDENTITY_INSERT`
- Includes a verification summary query at the end

### Deployment checklist

1. Ensure dev widget config is finalized via Widget Editor
2. Run `.\scripts\Export-WidgetConfig.ps1`
3. Review `scripts/deploy-widget-config.sql` (spot-check row counts)
4. Run the generated SQL against target database
5. Verify the summary query shows expected counts
