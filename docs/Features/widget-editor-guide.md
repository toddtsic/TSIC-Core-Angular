# Widget Editor — User Guide

**Route**: `/:jobPath/admin/widget-editor`
**Access**: SuperUser only (`SuperUserOnly` authorization policy)
**Last Updated**: 2026-02-26

---

## Overview

The Widget Editor is the admin tool for controlling which widgets appear on the public landing page and authenticated dashboards — and in what order. It manages three layers of configuration:

1. **Widget Definitions** — the master catalog of all widgets
2. **Default Matrix** — which widgets are enabled for each role, per job type
3. **Job Overrides** — per-job customizations that override the defaults

Changes here directly affect what end users see on their dashboard and what anonymous visitors see on the public landing page.

---

## Three Tabs

| Tab | Purpose |
|-----|---------|
| **Widget Definitions** | Create, edit, and delete widgets in the master catalog |
| **Default Matrix** | Configure which roles see which widgets (per job type), reorder widgets and categories |
| **Job Overrides** | Customize a specific job's widgets — enable, disable, add, or reorder beyond the defaults |

---

## Tab 1: Widget Definitions

### What it does
Manages the master list of all widget definitions in the system. Every widget that can appear on any dashboard or landing page must have a definition here.

### Key fields

| Field | Description |
|-------|-------------|
| **Name** | Display name (shown in the editor and dashboard) |
| **Widget Type** | `content`, `chart-tile`, or `status-tile` — controls rendering shape |
| **Component Key** | Maps to the Angular component in `widget-registry.ts` |
| **Category** | Groups the widget (e.g., "Bulletins", "Registration Charts") |
| **Description** | Optional — for admin reference only |
| **Dashboard Config** | JSON config with `icon` and `displayStyle` |
| **Role Assignments** | Which roles + job types this widget is assigned to |

### Scenarios

#### Scenario: Register a new widget that a developer just built

1. Go to **Widget Definitions** tab
2. If the widget is in the code manifest, it appears in the yellow **"manifest entries without widget definitions"** panel at the top
3. Click the widget's name in that panel — the Add Widget form opens pre-filled from the manifest (name, type, component key, icon, category)
4. Review the pre-filled values, adjust if needed
5. Under **Role Assignments**, select the job types and roles that should see this widget
6. Click **Create**

#### Scenario: Add a widget manually (not from manifest)

1. Click **Add Widget** button
2. In the Component Key dropdown, select **"Other (custom key)..."**
3. Enter all fields manually
4. Assign roles and job types
5. Click **Create**

> **Warning**: Custom keys require a matching entry in `widget-registry.ts` to actually render on the dashboard. Without it, the dashboard will skip the widget silently.

#### Scenario: Change which roles see a widget

1. Find the widget in the definitions table
2. Click the pencil (Edit) icon
3. Expand the **Role Assignments** section
4. Toggle job types and roles on/off using the chip buttons
5. The summary at the bottom shows the cross-product count (e.g., "6 assignments = 3 roles x 2 job types")
6. Click **Update**

#### Scenario: Delete a widget

1. Click the trash icon next to the widget
2. Confirm in the delete dialog
3. This removes the widget definition AND all its role assignments across all job types

#### Scenario: Change a widget's display style

1. Edit the widget
2. Under **Dashboard Config**, select a Display Style from the dropdown
   - `content` type: `banner`, `feed`, `block`
   - `chart-tile` type: `standard`, `wide`, `spark`
   - `status-tile` type: `standard`, `hero`, `compact`
3. Click **Update**

---

## Tab 2: Default Matrix

### What it does
Controls the **default** widget visibility per role for a given job type. This is the baseline — before any per-job overrides are applied.

The matrix shows a grid of widgets (rows) vs. roles (columns) with checkboxes. A checked box means that role sees that widget by default for all jobs of this type.

### How to use it

1. Select a **Job Type** from the dropdown (e.g., "Tournament", "League")
2. The matrix loads, organized by workspace (Public / Dashboard) and category
3. Expand/collapse workspaces by clicking the workspace header

### Scenarios

#### Scenario: Enable a widget for a role

1. Select the job type
2. Find the widget row and the role column
3. Check the checkbox
4. The sticky **Save bar** appears at the bottom showing unsaved changes count
5. Click **Save Defaults**

#### Scenario: Disable a widget for a role

1. Uncheck the checkbox for that widget+role combination
2. Click **Save Defaults**

#### Scenario: Reorder widgets within a category

Widgets within a category can be drag-reordered. This controls the display order on the dashboard.

1. Grab the grip handle (vertical dots icon) on the left of any widget row
2. Drag up or down within the category
3. The save bar appears — click **Save Defaults**

#### Scenario: Reorder categories within a workspace

Categories themselves can be reordered to control which group of widgets appears first. For example, moving "Registration Charts" above "Bulletins" on the public workspace.

1. When a workspace has multiple categories, each category header shows a drag handle (vertical dots)
2. Grab the category drag handle and drag up or down
3. A separate **"Category order changed"** save bar appears
4. Click **Save Category Order**

> **Important**: Category order is global — it affects ALL job types, not just the currently selected one.

#### Scenario: Copy defaults from another job type

1. Select the target job type (the one you want to configure)
2. Click **Copy from...** and select the source job type
3. The matrix populates with the source's configuration
4. Review and adjust as needed
5. Click **Save Defaults**

#### Scenario: Reset unsaved changes

- Click **Reset** in the save bar to discard all unsaved widget toggle/reorder changes
- Click **Reset** in the category order save bar to discard category reorder changes

---

## Tab 3: Job Overrides

### What it does
Allows per-job customization that overrides the Default Matrix. Use this when a specific job needs different widget configuration than the job type defaults.

The matrix looks similar to the Default Matrix but adds visual indicators showing which cells are inherited vs. overridden.

### Legend

| Indicator | Meaning |
|-----------|---------|
| No outline | Inherited from defaults (click to override) |
| Green outline | Override: explicitly enabled |
| Red outline + dimmed | Override: explicitly disabled |
| Blue outline | Job-specific addition (not in defaults) |

### How to use it

1. Select a **Job Type**, then select a specific **Job**
2. The override matrix loads, showing inherited defaults with override indicators
3. Click a checkbox to cycle through states

### Scenarios

#### Scenario: Disable a widget for one specific job

1. Select the job type and job
2. Find the widget+role checkbox (currently checked = inherited from defaults)
3. Click it — it becomes an override with the checkbox unchecked (red outline)
4. Click **Save Overrides**

This job's users won't see that widget, even though the default says they should.

#### Scenario: Add a widget to a specific job that isn't in the defaults

1. Find the widget row and a role column where the checkbox is unchecked
2. Click the checkbox — it becomes a "job-specific addition" (blue outline)
3. Click **Save Overrides**

#### Scenario: Revert a single override back to inherited

1. **Right-click** the overridden checkbox
2. It reverts to the inherited state (outline disappears)

#### Scenario: Reset all overrides for a job

1. Click **Reset All Overrides** in the toolbar
2. All overridden cells revert to inherited state
3. Click **Save Overrides**

This effectively makes the job use pure defaults again.

#### Scenario: Reorder widgets for a specific job

1. Drag widget rows within a category (same as Default Matrix)
2. Reordering automatically marks affected entries as overridden
3. Click **Save Overrides**

---

## Export SQL (Dev Mode Only)

Available when running in Angular dev mode. Generates an idempotent SQL script that captures the current widget configuration for deployment to other environments.

1. Click **Export SQL** in the page header
2. Wait for the script to generate
3. Click **Copy to Clipboard**
4. Run the SQL against the target database

See also: `scripts/Export-WidgetConfig.ps1` for a PowerShell-based export alternative.

---

## How Widget Configuration Flows to Users

```
Widget Definitions (master catalog)
    ↓
Default Matrix (per JobType + Role)
    ↓
Job Overrides (per Job, optional)
    ↓
UserWidget (per user, future — hide/reorder)
    ↓
What the user sees on their dashboard
```

### Merge Rules

1. **WidgetDefault** (Default Matrix) — baseline for all jobs of a type
2. **JobWidget** (Job Overrides) — admin per-job overrides (enable/disable, reorder, config)
3. **UserWidget** — per-user customizations (hide, reorder) — future feature

Each layer can override `DisplayOrder`, `Config`, and `CategoryId`. Later layers win.

---

## Database Tables

| Table | Purpose |
|-------|---------|
| `widgets.WidgetCategory` | Category definitions (name, workspace, default order, icon) |
| `widgets.Widget` | Widget definitions (name, type, component key, category, config) |
| `widgets.WidgetDefault` | Default role assignments per job type (the Default Matrix) |
| `widgets.JobWidget` | Per-job overrides |
| `widgets.UserWidget` | Per-user customizations (future) |

---

## API Endpoints

All endpoints require SuperUser authorization.

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/widget-editor/job-types` | List job types |
| GET | `/api/widget-editor/roles` | List roles |
| GET | `/api/widget-editor/categories` | List categories |
| PUT | `/api/widget-editor/categories/order` | Save category display order |
| GET | `/api/widget-editor/widgets` | List all widget definitions |
| POST | `/api/widget-editor/widgets` | Create a widget definition |
| PUT | `/api/widget-editor/widgets/{id}` | Update a widget definition |
| DELETE | `/api/widget-editor/widgets/{id}` | Delete a widget definition |
| GET | `/api/widget-editor/defaults/{jobTypeId}` | Get default matrix for a job type |
| PUT | `/api/widget-editor/defaults/{jobTypeId}` | Save default matrix |
| GET | `/api/widget-editor/widgets/{id}/assignments` | Get role assignments for a widget |
| PUT | `/api/widget-editor/widgets/{id}/assignments` | Save role assignments (bulk) |
| GET | `/api/widget-editor/jobs-by-type/{jobTypeId}` | List jobs for a job type |
| GET | `/api/widget-editor/job-overrides/{jobId}` | Get overrides for a job |
| PUT | `/api/widget-editor/job-overrides/{jobId}` | Save overrides for a job |
| GET | `/api/widget-editor/export-sql` | Generate deployment SQL (dev only) |

---

## Common Workflows

### "I want bulletins to appear below charts on the public page"

1. Go to **Default Matrix** tab, select any job type
2. In the **Public** workspace, drag the "Bulletins" category below the charts category using the category drag handle
3. Click **Save Category Order**
4. Category order is global — this change applies to all jobs

### "I want to add a new chart widget to all tournament dashboards"

1. Go to **Widget Definitions** tab
2. If the widget appears in the manifest panel, click it to auto-fill; otherwise click **Add Widget**
3. Fill in details, assign to "Dashboard" category
4. Under Role Assignments, select "Tournament" job type and the desired roles
5. Click **Create**
6. Go to **Default Matrix** tab, select "Tournament"
7. Verify the widget appears with the correct checkboxes

### "One specific job shouldn't show the year-over-year chart"

1. Go to **Job Overrides** tab
2. Select the job type, then the specific job
3. Find the year-over-year widget row
4. Uncheck the checkbox for each role that shouldn't see it
5. Click **Save Overrides**

### "I want to change widget order for just one job"

1. Go to **Job Overrides** tab
2. Select the job type and job
3. Drag widgets to reorder within a category
4. Click **Save Overrides**
5. This job now has custom order; other jobs of the same type keep the default order

---

## Related Documentation

- [Widget Component Taxonomy](../Standards/WIDGET-COMPONENT-TAXONOMY.md) — WidgetType, Workspace, displayStyle architecture
- [Widget Dashboard Design Vision](../Design-New/WIDGET-WEBSITE-BUILDER-VISION.md) — future direction
- [Widget Specs](../Plans/NEW-WIDGET-SPECS-2026-02-17.md) — specs for planned widgets
