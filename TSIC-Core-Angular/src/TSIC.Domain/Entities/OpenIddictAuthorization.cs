using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class OpenIddictAuthorization
{
    public string Id { get; set; } = null!;

    public string? Scope { get; set; }

    public virtual ICollection<OpenIddictToken> OpenIddictTokens { get; set; } = new List<OpenIddictToken>();
}
