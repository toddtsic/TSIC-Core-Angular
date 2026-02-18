# Widget Component Taxonomy & Directory Structure

> **Status**: Approved (v2 — WidgetType consolidation + displayStyle)
> **Date**: 2026-02-18
> **Applies to**: All widget components rendered by the Widget Dashboard

---

## Core Concepts

### Two Independent Axes

**Axis 1 — Domain Function** (what is this widget *about*?)
Maps to `WidgetCategory.Workspace` in the database. Determines where code lives.

**Axis 2 — Rendering Pattern** (what *shape* does it take?)
Maps to `Widget.WidgetType` in the database. Determines dashboard layout behavior.

**Visual Variant** (how does it *look* within that shape?)
Carried in `Config` JSON as `displayStyle`. Overridable per job/role.

These are independent. A registration widget could be a chart-tile or a link-tile.
A chart-tile shell could render registration data or financial data.

---

## WidgetType (Locked — 4 Values)

| WidgetType | Shape | Description |
|---|---|---|
| `content` | Full-width inline block | Renders custom component inline, no tile chrome |
| `chart-tile` | Bounded grid tile | Data visualization with click-to-expand interaction |
| `status-tile` | Bounded grid tile | Single metric/KPI with trend indicator |
| `link-tile` | Bounded grid tile | Click → navigate to a routed view (doorbell pattern) |

### Consolidation History

| Old Value | New Value | Reason |
|---|---|---|
| `quick-action` | `link-tile` | Was renamed to `action-card`, then absorbed — same behavior as link |
| `workflow-pipeline` | `link-tile` | Was renamed to `pipeline-card`, then absorbed — same behavior as link |
| `link-group` | `link-tile` | Was renamed to `link-card`, now `link-tile` |
| `action-card` | `link-tile` | Intermediate name, absorbed into link-tile |
| `pipeline-card` | `link-tile` | Intermediate name, absorbed into link-tile |
| `status-card` | `status-tile` | Renamed for `-tile` consistency |
| `chart` | `chart-tile` | Renamed — it IS a tile first, chart is what's inside |
| `content` | `content` | Unchanged — genuinely NOT a tile |

---

## displayStyle (Config JSON)

The `displayStyle` property in Config JSON controls visual variant within each
WidgetType. When absent, defaults to `standard`. Overridable at three levels:
`Widget.DefaultConfig` → `WidgetDefault.Config` → `JobWidget.Config`.

### content

| displayStyle | Rendering |
|---|---|
| `banner` | Hero image/logo block (client-banner) |
| `feed` | Scrollable item list (bulletins, announcements) |
| `block` | Static text/HTML panel (future rich-text content) |

### chart-tile

| displayStyle | Rendering |
|---|---|
| `standard` | Default tile with click-to-expand modal |
| `wide` | Spans full grid width (complex visualizations) |
| `spark` | Compact inline sparkline, no expand (future mini-charts) |

### status-tile

| displayStyle | Rendering |
|---|---|
| `standard` | Icon + metric + label + trend indicator |
| `hero` | Large featured metric, prominent placement |
| `compact` | Number + label only, denser grid |

### link-tile

| displayStyle | Rendering |
|---|---|
| `standard` | Icon + label + description (current look) |
| `hero` | Large CTA tile, prominent styling (e.g. "Register Now") |
| `compact` | Icon + label only, no description, denser grid |

---

## Directory Structure

```
src/app/widgets/
├── shared/                          ← Reusable rendering shells (Axis 2)
│   ├── chart-tile/                  ← Chart visualization tile
│   ├── status-tile/                 ← KPI metric tile
│   └── link-tile/                   ← Navigation tile
│
├── registration/                    ← Domain: player/team registration
│   ├── player-trend-widget/         ← Uses chart-tile shell
│   ├── team-trend-widget/           ← Uses chart-tile shell
│   └── agegroup-distribution-widget/
│
├── scheduling/                      ← Domain: scheduling & events
│   └── year-over-year-widget/
│
├── event-info/                      ← Domain: event identity & contact
│   └── event-contact-widget/
│
├── communications/                  ← Domain: bulletins & announcements
│   └── bulletins.component.ts
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

- The **host shell** component (hub/spoke/public mode switching, grid layout,
  `@switch` dispatch by `componentKey`)
- `widget-dashboard.service.ts` (data fetching for widget components)
- Route configuration

The host **imports from `@widgets/`** — it does not contain widget implementations.

### What stays in `layouts/components/`

Platform-level chrome consistent across ALL jobs (NOT widgets):
- `client-header-bar/` — app header with auth, palette, theme
- `client-footer-bar/` — app footer with copyright
- `client-menu/` — legacy menu (superseded by widget system)

---

## How This Aligns With the Database

```
DB                          Code
──────────────────────────  ──────────────────────────
WidgetCategory.Workspace    → widgets/{domain}/        (directory)
Widget.WidgetType           → tile shape / layout hint (content|chart-tile|status-tile|link-tile)
Widget.ComponentKey         → @switch case in host     (dispatch key)
Widget.DefaultConfig        → JSON props to component  (icon, route, label, displayStyle)
WidgetDefault.Config        → per-role override         (displayStyle, readOnly, etc.)
JobWidget.Config            → per-job override          (displayStyle, custom labels, etc.)
```

When adding a new widget:

1. **Identify its domain** → create/find directory under `widgets/`
2. **Pick its WidgetType** → `content`, `chart-tile`, `status-tile`, or `link-tile`
3. **Pick displayStyle** → set in `DefaultConfig` JSON (defaults to `standard` if omitted)
4. **Register in DB** → INSERT into `widgets.Widget` with ComponentKey
5. **Add to host `@switch`** → one new case mapping `componentKey` → component

---

## Rules

1. **Organize by domain, not by rendering pattern.** A "Registration Revenue Chart"
   goes in `widgets/registration/`, not `widgets/charts/`.

2. **Rendering shells are generic.** `chart-tile` knows nothing about registration
   or scheduling — it takes data inputs and renders a chart. Domain logic stays
   in the widget component that wraps the shell.

3. **`WidgetType` is a tile shape, not an identity.** It tells the dashboard
   host how to size/layout the widget. It does NOT determine where the code lives.

4. **`displayStyle` is a visual variant, not a type.** A link-tile that looks
   like a hero CTA and one that looks compact are both `link-tile` — the visual
   difference lives in Config JSON, not in WidgetType.

5. **Domain directories mirror workspaces.** If a new workspace is added to
   `WidgetCategory`, a corresponding directory appears under `widgets/`.

6. **The host shell is a thin dispatcher.** `widget-dashboard.component.ts` has a
   `@switch` on `componentKey` and nothing else. All rendering logic lives in the
   widget components themselves.

7. **Four WidgetTypes only.** Do not add new WidgetType values without explicit
   architectural review. Visual variations belong in `displayStyle`.
