using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ApiResource
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    public string Name { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public DateTime Created { get; set; }

    public DateTime? Updated { get; set; }

    public DateTime? LastAccessed { get; set; }

    public bool NonEditable { get; set; }

    public virtual ICollection<ApiClaim> ApiClaims { get; set; } = new List<ApiClaim>();

    public virtual ICollection<ApiProperty> ApiProperties { get; set; } = new List<ApiProperty>();

    public virtual ICollection<ApiScope> ApiScopes { get; set; } = new List<ApiScope>();

    public virtual ICollection<ApiSecret> ApiSecrets { get; set; } = new List<ApiSecret>();
}
