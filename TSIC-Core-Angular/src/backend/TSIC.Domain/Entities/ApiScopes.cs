using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ApiScopes
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public bool Required { get; set; }

    public bool Emphasize { get; set; }

    public bool ShowInDiscoveryDocument { get; set; }

    public int ApiResourceId { get; set; }

    public virtual ApiResources ApiResource { get; set; } = null!;

    public virtual ICollection<ApiScopeClaims> ApiScopeClaims { get; set; } = new List<ApiScopeClaims>();
}
