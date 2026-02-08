namespace TSIC.Domain.Constants;

/// <summary>
/// Menu type identifiers from the MenuTypes reference table.
/// These values correspond to fixed rows in the database and never change.
/// </summary>
public static class MenuTypeConstants
{
    /// <summary>
    /// Public menu (MenuTypeId = 1)
    /// </summary>
    public const int PublicMenu = 1;

    /// <summary>
    /// Nav main logged out (MenuTypeId = 2)
    /// </summary>
    public const int NavMainLoggedOut = 2;

    /// <summary>
    /// Nav main logged in (MenuTypeId = 3)
    /// </summary>
    public const int NavMainLoggedIn = 3;

    /// <summary>
    /// Nav footer logged out (MenuTypeId = 4)
    /// </summary>
    public const int NavFooterLoggedOut = 4;

    /// <summary>
    /// Nav footer logged in (MenuTypeId = 5)
    /// </summary>
    public const int NavFooterLoggedIn = 5;

    /// <summary>
    /// Per login role (MenuTypeId = 6)
    /// Used for job-specific, role-based menus managed via the Menu Admin page.
    /// </summary>
    public const int PerLoginRole = 6;

    /// <summary>
    /// Nav login status logged in (MenuTypeId = 7)
    /// </summary>
    public const int NavLoginStatusLoggedIn = 7;

    /// <summary>
    /// Nav login status logged out (MenuTypeId = 8)
    /// </summary>
    public const int NavLoginStatusLoggedOut = 8;

    /// <summary>
    /// Public menu 2 (MenuTypeId = 9)
    /// </summary>
    public const int PublicMenu2 = 9;
}
