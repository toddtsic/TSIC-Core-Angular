using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ApiResources
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

    public virtual ICollection<ApiClaims> ApiClaims { get; set; } = new List<ApiClaims>();

    public virtual ICollection<ApiProperties> ApiProperties { get; set; } = new List<ApiProperties>();

    public virtual ICollection<ApiScopes> ApiScopes { get; set; } = new List<ApiScopes>();

    public virtual ICollection<ApiSecrets> ApiSecrets { get; set; } = new List<ApiSecrets>();
}
