namespace TSIC.Domain.Constants;

/// <summary>
/// Widget workspace identifiers stored in widgets.WidgetCategory.Workspace.
/// The workspace partitions widgets into what a viewer can see:
/// public content (anyone, including anonymous visitors) vs authenticated
/// role-specific dashboards. Values are case-sensitive and must match the
/// DB column exactly.
/// </summary>
public static class WidgetWorkspaces
{
    /// <summary>Shown to every viewer regardless of auth state.</summary>
    public const string Public = "public";

    /// <summary>Shown only inside the authenticated role-specific dashboard tab.</summary>
    public const string Dashboard = "dashboard";
}
