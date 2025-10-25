using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Family
{
    public string FamilyUserId { get; set; } = null!;

    public string? MomFirstName { get; set; }

    public string? MomLastName { get; set; }

    public string? MomCellphone { get; set; }

    public string? MomCellphoneProvider { get; set; }

    public string? MomEmail { get; set; }

    public string? DadFirstName { get; set; }

    public string? DadLastName { get; set; }

    public string? DadCellphone { get; set; }

    public string? DadCellphoneProvider { get; set; }

    public string? DadEmail { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual CellphonecarrierDomain? DadCellphoneProviderNavigation { get; set; }

    public virtual ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();

    public virtual AspNetUser FamilyUser { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }

    public virtual CellphonecarrierDomain? MomCellphoneProviderNavigation { get; set; }

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}
