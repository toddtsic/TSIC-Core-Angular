using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Families
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

    public virtual CellphonecarrierDomains? DadCellphoneProviderNavigation { get; set; }

    public virtual ICollection<FamilyMembers> FamilyMembers { get; set; } = new List<FamilyMembers>();

    public virtual AspNetUsers FamilyUser { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual CellphonecarrierDomains? MomCellphoneProviderNavigation { get; set; }

    public virtual ICollection<Registrations> Registrations { get; set; } = new List<Registrations>();
}
