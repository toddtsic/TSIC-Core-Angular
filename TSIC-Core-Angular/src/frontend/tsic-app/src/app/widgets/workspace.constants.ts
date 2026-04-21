// Widget workspace identifiers. Must match TSIC.Domain.Constants.WidgetWorkspaces
// on the backend — these strings are persisted in widgets.WidgetCategory.Workspace
// and returned in WidgetWorkspaceDto.workspace.

export const Workspaces = {
    Public: 'public',
    Dashboard: 'dashboard',
} as const;

export type WorkspaceKey = typeof Workspaces[keyof typeof Workspaces];
