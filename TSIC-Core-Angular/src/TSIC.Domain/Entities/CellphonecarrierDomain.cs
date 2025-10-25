using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CellphonecarrierDomain
{
    public string Carrier { get; set; } = null!;

    public string? Domain { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public int Aid { get; set; }

    public virtual ICollection<Family> FamilyDadCellphoneProviderNavigations { get; set; } = new List<Family>();

    public virtual ICollection<Family> FamilyMomCellphoneProviderNavigations { get; set; } = new List<Family>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PersonContact> PersonContactCeCellphoneProviderNavigations { get; set; } = new List<PersonContact>();

    public virtual ICollection<PersonContact> PersonContactCpCellphoneProviderNavigations { get; set; } = new List<PersonContact>();

    public virtual ICollection<PersonContact> PersonContactCsCellphoneProviderNavigations { get; set; } = new List<PersonContact>();
}
