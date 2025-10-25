using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class IdentityProperty
{
    public int Id { get; set; }

    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public int IdentityResourceId { get; set; }

    public virtual IdentityResource IdentityResource { get; set; } = null!;
}
