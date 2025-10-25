using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetRoleClaim
{
    public string? ClaimType { get; set; }

    public string? ClaimValue { get; set; }

    public int Id { get; set; }

    public string? RoleId { get; set; }

    public virtual AspNetRole? Role { get; set; }
}
