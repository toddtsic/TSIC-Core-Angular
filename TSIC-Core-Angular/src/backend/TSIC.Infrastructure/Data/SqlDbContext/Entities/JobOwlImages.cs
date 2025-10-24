using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobOwlImages
{
    public Guid JobId { get; set; }

    public string? Caption { get; set; }

    public int OwlSlideCount { get; set; }

    public string? OwlImage01 { get; set; }

    public string? OwlImage02 { get; set; }

    public string? OwlImage03 { get; set; }

    public string? OwlImage04 { get; set; }

    public string? OwlImage05 { get; set; }

    public string? OwlImage06 { get; set; }

    public string? OwlImage07 { get; set; }

    public string? OwlImage08 { get; set; }

    public string? OwlImage09 { get; set; }

    public string? OwlImage10 { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;
}
