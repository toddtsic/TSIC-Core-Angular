using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class OpenIddictAuthorizations
{
    public string Id { get; set; } = null!;

    public string? Scope { get; set; }

    public virtual ICollection<OpenIddictTokens> OpenIddictTokens { get; set; } = new List<OpenIddictTokens>();
}
