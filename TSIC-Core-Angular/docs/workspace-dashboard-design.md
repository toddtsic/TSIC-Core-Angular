# Widget Dashboard + Workspace Architecture

## Core Concept

The widget system uses a **Dashboard + Workspaces** model:

- **Dashboard** = the ONLY view called "Dashboard." Status-only, like a car dashboard — KPIs, health indicators, bulletins. Tells you how things are going; doesn't give you tools.
- **Workspaces** = purpose-driven navigable areas where you actually do things (manage registrations, configure events, schedule games, etc.).

Users arrive at the Dashboard by default and navigate to Workspaces as needed. The UI clearly advertises available workspaces so users can quickly reach the function they came for.

## Workspaces

| Key | Name | Purpose |
|-----|------|---------|
| `dashboard` | Dashboard | KPIs, health indicators, bulletins — read-only status glance |
| `job-config` | Job Configuration | Event setup, LADT editor, fee structures |
| `player-reg` | Player Registration | Player registration management, search, trends |
| `team-reg` | Team Registration | Team/club registration management |
| `scheduling` | Scheduling | Scheduling pipeline, pool assignments, game/field management |
| `fin-per-job` | Organization Finances | Org-level revenue, outstanding balances, payment summaries |
| `fin-per-customer` | My Finances | Individual customer view: "what do I owe?" |
| `public` | Public Landing | Anonymous landing page (banner + bulletins). Not a workspace — no auth required. |

## Role-to-Workspace Matrix

Two-tier gating: **Role determines which workspaces are visible**, then **Role + Workspace determines which widgets appear**.

| Role | Dashboard | Job Config | Player Reg | Team Reg | Scheduling | Fin-PerJob | Fin-PerCustomer |
|------|-----------|------------|------------|----------|------------|------------|-----------------|
| **SuperUser** | yes | yes | yes | yes | yes | yes | yes |
| **SuperDirector** | yes | yes | yes | yes | yes | yes | yes |
| **Director** | yes | yes | yes | yes | yes | yes | - |
| **Club Rep** | yes | - | yes | yes | yes (view) | - | - |
| **Player** | yes | - | yes | - | yes (view) | - | - |
| **Staff** | yes | - | yes (view) | yes (view) | yes (view) | - | - |
| **Unassigned Adult** | yes | - | - | - | - | - | - |
| **Family** | - | - | - | - | - | - | - |

### Role Notes
- **Family** has no landing page. They enter via bulletin links on the anonymous public landing and register. After registration, they log in and are presented as their Player roles.
- **Unassigned Adult** (Guest role) sees Dashboard only — bulletins and basic status. A Director must elevate them to Staff before they get operational access.
- **Staff** includes coaches. They see player rosters, team info, and schedules, all view-only.
- **(view)** suffix means the workspace is accessible but with read-only widgets (no tools/actions).

## Design Rules

1. **Report widgets are parameterized** — pass report name via Config JSON. Reports live in the workspace where they're relevant, NOT in a separate "Reports" workspace.
2. **Widgets can appear in multiple workspaces** — same widget, different WidgetDefault entries pointing to different categories. Different Config per placement if needed.
3. **Banner widget**: anonymous public landing only. Authenticated users don't need it.
4. **Bulletins widget**: appears in both `public` (anonymous) and `dashboard` (authenticated).
5. **Dashboard status widgets** should surface summary KPIs from workspaces the user has access to. If something needs attention, the user navigates to the relevant workspace.

## Database Schema

Widgets live in the `[widgets]` schema with 4 tables:

- **WidgetCategory** — Groups widgets within a workspace. Has `Workspace` column (e.g., 'dashboard', 'player-reg').
- **Widget** — Widget definitions with ComponentKey for frontend rendering.
- **WidgetDefault** — Role + JobType → Widget + Category mapping (global defaults).
- **JobWidget** — Per-job overrides (enable/disable, reorder, override config).

### Three-Tier Merge
```
WidgetDefault (Role + JobType global config)
    + JobWidget (Per-Job overrides)
    = Merged result grouped by Workspace → Category → Widget
```

## Widget Types

| Type | Use Case |
|------|----------|
| `content` | Full-width display: banner, bulletins |
| `chart` | Data visualization: trend charts, distribution charts |
| `status-card` | KPI/health metric cards |
| `quick-action` | Navigation/action cards that route to tools |
| `workflow-pipeline` | Workflow process visualization |
| `link-group` | Groups of action links |
