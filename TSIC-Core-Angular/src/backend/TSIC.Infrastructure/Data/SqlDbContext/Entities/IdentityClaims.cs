using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class IdentityClaims
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public int IdentityResourceId { get; set; }

    public virtual IdentityResources IdentityResource { get; set; } = null!;
}
