# Widget Component Taxonomy & Directory Structure

> **Status**: Approved
> **Date**: 2026-02-18
> **Applies to**: All widget components rendered by the Widget Dashboard

---

## The Problem

Widget components were accumulating in `views/home/widget-dashboard/` alongside
the dashboard host component. As more widgets are added across workspaces and
roles, we need a principled organization that scales.

The existing `WidgetType` field (`chart`, `content`, `status-card`, `quick-action`,
`link-group`, `workflow-pipeline`) mixes orthogonal concepts:

- `chart` and `content` describe **rendering** (how it draws)
- `quick-action` describes **interaction** (what happens on click)
- `link-group` and `workflow-pipeline` describe **structure** (how data is shaped)

These aren't peers — "should this be a chart or a quick-action?" is a category error.

---

## Two Independent Axes

### Axis 1: Domain Function (what is this widget *about*?)

Maps to `WidgetCategory.Workspace` in the database. This is the **primary
organizing principle** for code — it determines where files live, who maintains
them, and what data they consume.

| Domain | Example widgets |
|---|---|
| `registration` | Player trend, team trend, agegroup distribution |
| `scheduling` | Year-over-year comparison, upcoming events |
| `event-info` | Event contact, venue details, branding |
| `communications` | Bulletins, announcements |
| `finance` | Outstanding balances, revenue summary |

### Axis 2: Rendering Pattern (how does it *present*?)

Maps to `Widget.WidgetType` in the database. This determines which **shared
visual shell** the widget uses — sizing, layout behavior, and interaction model.

| Pattern | Shell component | Behavior |
|---|---|---|
| `chart` | `chart-card` | Collapsible card with KPI summary badges + chart overlay |
| `content` | `info-card` | Key-value display, text blocks, static information |
| `status-card` | `status-card` | Single KPI metric with trend indicator |
| `navigation` | `link-card` | List of clickable links derived from config |
| `action` | `action-card` | Clickable trigger that initiates a workflow |
| `pipeline` | `pipeline-card` | Step-by-step sequence visualization |

These are **independent**. A registration widget could be a chart, a card, or a
link group. A chart shell could render registration data, financial data, or
scheduling data.

---

## Directory Structure

```
src/app/widgets/
├── shared/                          ← Reusable rendering shells (Axis 2)
│   ├── chart-card/                  ← Collapsible chart + KPI badges
│   ├── info-card/                   ← Key-value / text display
│   ├── link-card/                   ← Navigation link list from config
│   ├── action-card/                 ← Clickable action triggers
│   ├── status-card/                 ← Single-KPI metric display
│   └── pipeline-card/               ← Step sequence visualization
│
├── registration/                    ← Domain: player/team registration
│   ├── player-trend-widget/         ← Uses chart-card shell
│   ├── team-trend-widget/           ← Uses chart-card shell
│   └── agegroup-distribution-widget/
│
├── scheduling/                      ← Domain: scheduling & events
│   └── year-over-year-widget/
│
├── event-info/                      ← Domain: event identity & contact
│   └── event-contact-widget/
│
├── communications/                  ← Domain: bulletins & announcements
│   └── bulletins-widget/
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

---

## How This Aligns With the Database

```
DB                          Code
──────────────────────────  ──────────────────────────
WidgetCategory.Workspace    → widgets/{domain}/        (directory)
Widget.WidgetType           → widgets/shared/{shell}/  (rendering shell)
Widget.ComponentKey         → @switch case in host     (dispatch key)
Widget.DefaultConfig        → JSON props to shell      (icon, route, label)
```

When adding a new widget:

1. **Identify its domain** → create/find directory under `widgets/`
2. **Pick its rendering shell** → import from `widgets/shared/`
3. **Register in DB** → `WidgetType` = rendering hint, `CategoryId` = domain category
4. **Add to host `@switch`** → one new case mapping `componentKey` → component

---

## Rules

1. **Organize by domain, not by rendering pattern.** A "Registration Revenue Chart"
   goes in `widgets/registration/`, not `widgets/charts/`.

2. **Rendering shells are generic.** `chart-card` knows nothing about registration
   or scheduling — it takes data inputs and renders a chart. Domain logic stays
   in the widget component that wraps the shell.

3. **`WidgetType` is a rendering hint, not an identity.** It tells the dashboard
   host how to size/layout the widget. It does NOT determine where the code lives.

4. **Domain directories mirror workspaces.** If a new workspace is added to
   `WidgetCategory`, a corresponding directory appears under `widgets/`.

5. **The host shell is a thin dispatcher.** `widget-dashboard.component.ts` has a
   `@switch` on `componentKey` and nothing else. All rendering logic lives in the
   widget components themselves.
