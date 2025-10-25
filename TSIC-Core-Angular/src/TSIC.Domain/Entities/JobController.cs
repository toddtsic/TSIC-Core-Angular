using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobController
{
    public string Controller { get; set; } = null!;

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual AspNetUser? LebUser { get; set; }
}
