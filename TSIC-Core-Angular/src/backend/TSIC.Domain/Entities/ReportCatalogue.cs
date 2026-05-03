using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ReportCatalogue
{
    public Guid ReportId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? IconName { get; set; }

    public string StoredProcName { get; set; } = null!;

    public string? ParametersJson { get; set; }

    public string? VisibilityRules { get; set; }

    public int SortOrder { get; set; }

    public bool Active { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public string? CategoryCode { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }
}
