using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class AspNetUserLogins
{
    public string LoginProvider { get; set; } = null!;

    public string? ProviderDisplayName { get; set; }

    public string ProviderKey { get; set; } = null!;

    public string? UserId { get; set; }

    public virtual AspNetUsers? User { get; set; }
}
