using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ApiScopeClaims
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public int ApiScopeId { get; set; }

    public virtual ApiScopes ApiScope { get; set; } = null!;
}
