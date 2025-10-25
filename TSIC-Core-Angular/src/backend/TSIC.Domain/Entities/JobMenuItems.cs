using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobMenuItems
{
    public string? Action { get; set; }

    public string? Controller { get; set; }

    public string? ImageUrl { get; set; }

    public string? NavigateUrl { get; set; }

    public int? ReportExportTypeId { get; set; }

    public string? Target { get; set; }

    public string? Text { get; set; }

    public bool Active { get; set; }

    public bool BCollapsed { get; set; }

    public bool BTextWrap { get; set; }

    public int? Index { get; set; }

    public string? LebUserId { get; set; }

    public Guid? MenuId { get; set; }

    public Guid MenuItemId { get; set; }

    public DateTime Modified { get; set; }

    public Guid? ParentMenuItemId { get; set; }

    public string? ReportName { get; set; }

    public string? RouterLink { get; set; }

    public string? IconName { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual JobMenus? Menu { get; set; }

    public virtual ReportExportTypes? ReportExportType { get; set; }

    public virtual Reports? ReportNameNavigation { get; set; }
}
