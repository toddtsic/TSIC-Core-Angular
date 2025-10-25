using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class IdentityResources
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    public string Name { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public bool Required { get; set; }

    public bool Emphasize { get; set; }

    public bool ShowInDiscoveryDocument { get; set; }

    public DateTime Created { get; set; }

    public DateTime? Updated { get; set; }

    public bool NonEditable { get; set; }

    public virtual ICollection<IdentityClaims> IdentityClaims { get; set; } = new List<IdentityClaims>();

    public virtual ICollection<IdentityProperties> IdentityProperties { get; set; } = new List<IdentityProperties>();
}
