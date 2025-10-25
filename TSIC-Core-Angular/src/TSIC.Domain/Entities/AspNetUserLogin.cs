using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetUserLogin
{
    public string LoginProvider { get; set; } = null!;

    public string? ProviderDisplayName { get; set; }

    public string ProviderKey { get; set; } = null!;

    public string? UserId { get; set; }

    public virtual AspNetUser? User { get; set; }
}
