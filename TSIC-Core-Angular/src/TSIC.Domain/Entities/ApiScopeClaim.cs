using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ApiScopeClaim
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public int ApiScopeId { get; set; }

    public virtual ApiScope ApiScope { get; set; } = null!;
}
