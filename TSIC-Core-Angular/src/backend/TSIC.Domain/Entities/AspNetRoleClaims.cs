using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetRoleClaims
{
    public string? ClaimType { get; set; }

    public string? ClaimValue { get; set; }

    public int Id { get; set; }

    public string? RoleId { get; set; }

    public virtual AspNetRoles? Role { get; set; }
}
