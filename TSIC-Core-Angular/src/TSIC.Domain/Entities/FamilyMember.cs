using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class FamilyMember
{
    public int Id { get; set; }

    public string FamilyUserId { get; set; } = null!;

    public string FamilyMemberUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUser FamilyMemberUser { get; set; } = null!;

    public virtual Family FamilyUser { get; set; } = null!;
}
