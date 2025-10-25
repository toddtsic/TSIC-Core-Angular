using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class IdentityProperties
{
    public int Id { get; set; }

    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public int IdentityResourceId { get; set; }

    public virtual IdentityResources IdentityResource { get; set; } = null!;
}
