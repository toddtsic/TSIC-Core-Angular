using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class States
{
    public string? State { get; set; }

    public string StateId { get; set; } = null!;
}
