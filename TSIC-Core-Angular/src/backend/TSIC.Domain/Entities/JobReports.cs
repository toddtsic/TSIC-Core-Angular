using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobReports
{
    public Guid JobReportId { get; set; }

    public Guid JobId { get; set; }

    public string RoleId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? IconName { get; set; }

    public string Controller { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string Kind { get; set; } = null!;

    public string? GroupLabel { get; set; }

    public int SortOrder { get; set; }

    public bool Active { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual AspNetRoles Role { get; set; } = null!;
}
