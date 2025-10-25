using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class IdentityClaim
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public int IdentityResourceId { get; set; }

    public virtual IdentityResource IdentityResource { get; set; } = null!;
}
