using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class AspNetUserClaims
{
    public string? ClaimType { get; set; }

    public string? ClaimValue { get; set; }

    public int Id { get; set; }

    public string? UserId { get; set; }

    public virtual AspNetUsers? User { get; set; }
}
