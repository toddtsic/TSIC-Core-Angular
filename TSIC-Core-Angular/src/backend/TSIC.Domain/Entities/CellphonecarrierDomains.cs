using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CellphonecarrierDomains
{
    public string Carrier { get; set; } = null!;

    public string? Domain { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public int Aid { get; set; }

    public virtual ICollection<Families> FamiliesDadCellphoneProviderNavigation { get; set; } = new List<Families>();

    public virtual ICollection<Families> FamiliesMomCellphoneProviderNavigation { get; set; } = new List<Families>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<PersonContacts> PersonContactsCeCellphoneProviderNavigation { get; set; } = new List<PersonContacts>();

    public virtual ICollection<PersonContacts> PersonContactsCpCellphoneProviderNavigation { get; set; } = new List<PersonContacts>();

    public virtual ICollection<PersonContacts> PersonContactsCsCellphoneProviderNavigation { get; set; } = new List<PersonContacts>();
}
