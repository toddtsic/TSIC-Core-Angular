using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class FamilyMembers
{
    public int Id { get; set; }

    public string FamilyUserId { get; set; } = null!;

    public string FamilyMemberUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers FamilyMemberUser { get; set; } = null!;

    public virtual Families FamilyUser { get; set; } = null!;
}
