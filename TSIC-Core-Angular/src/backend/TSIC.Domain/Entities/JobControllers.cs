using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobControllers
{
    public string Controller { get; set; } = null!;

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }
}
