using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ReportLibrary
{
    public Guid ReportLibraryId { get; set; }

    public string ReportKey { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? Tags { get; set; }

    public string CategoryCode { get; set; } = null!;

    public string? IconName { get; set; }

    public string Kind { get; set; } = null!;

    public string Controller { get; set; } = null!;

    public string Action { get; set; } = null!;

    public Guid? OwnerCustomerId { get; set; }

    public int SortOrder { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public string Scope { get; set; } = null!;

    public string? MinRoleId { get; set; }

    public virtual ICollection<JobReports> JobReports { get; set; } = new List<JobReports>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual AspNetRoles? MinRole { get; set; }

    public virtual Customers? OwnerCustomer { get; set; }

    public virtual ICollection<JobTypes> JobType { get; set; } = new List<JobTypes>();
}
