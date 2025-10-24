using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class OpenIddictScopes
{
    public string Id { get; set; } = null!;

    public string? Description { get; set; }
}
