# Player Registration: Teams UI

This document summarizes the current Teams step in the Player Registration wizard.

## Overview

- Uses Syncfusion EJ2 Angular components:
  - Single-select: `DropDownList`
  - Multi-select (CAC): `MultiSelect` in CheckBox mode
- Capacity awareness:
  - Shows a badge with remaining capacity (e.g., "5 spots left").
  - Teams at capacity render a red "FULL" badge and are disabled from selection.
  - Team names for FULL entries are shown with strikethrough for quick scanning.
- Filtering:
  - Client-side filtering with `allowFiltering` and `filterType: 'Contains'`.
  - Placeholder text clarifies search intent (team name or year).
- Eligibility:
  - When a team constraint is configured (BYGRADYEAR/BYAGEGROUP/BYCLUBNAME), the list is filtered per player.

## UX specifics

- Popup item layout (one line): checkbox (in CAC) + team name + capacity badge.
- The capacity badge uses Bootstrap subtle styles (warning for low capacity, danger for FULL).
- Selected teams list (CAC):
  - The default truncated display inside the MultiSelect is hidden.
  - A custom list of selected teams is rendered beneath the control as badges with a small × to remove.

## Data flow

- Single source of truth: `RegistrationWizardService.selectedTeams` (signal).
- Bindings:
  - `[value]="selectedArrayFor(playerId)"`
  - `(change)="onSyncMultiChange(playerId, $event)"` (for MultiSelect)
  - `(change)="onSyncSingleChange(playerId, $event)"` (for DropDownList)
- The custom remove button calls `removeTeam(playerId, teamId)`, which delegates to `onSyncMultiChange(...)` to preserve validation and capacity checks.

## Capacity guard

- FULL teams are disabled via `fields.disabled = 'rosterIsFull'`.
- Additional guard prevents picking teams when remaining slots are very low (`capacityGuardThreshold`, default 10) and a tentative selection would exceed the max roster.

## Styling notes

- The popup uses flex to keep checkbox, name, and badge aligned on one line.
- Temporary yellow highlights used during development have been removed.
- Popup z-index is raised to render above sticky UI.

## Dev tips

- If the checkbox list appears without checkboxes, ensure `CheckBoxSelectionService` is provided in the component.
- To tweak badge spacing: adjust `margin-left` on the badge and the flex-row `gap` on `.rw-item`.
- To show selected names inline in the input instead of the custom list, remove the CSS rule hiding `.e-delim-values` and delete the custom list block in the template.
