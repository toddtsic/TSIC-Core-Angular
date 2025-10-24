using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ApiClaims
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public int ApiResourceId { get; set; }

    public virtual ApiResources ApiResource { get; set; } = null!;
}
