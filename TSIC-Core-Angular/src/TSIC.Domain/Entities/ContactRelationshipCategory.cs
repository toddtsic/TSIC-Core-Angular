using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ContactRelationshipCategory
{
    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? Relationship { get; set; }

    public Guid RelationshipId { get; set; }

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PersonContact> PersonContactCeRelationships { get; set; } = new List<PersonContact>();

    public virtual ICollection<PersonContact> PersonContactCpRelationships { get; set; } = new List<PersonContact>();

    public virtual ICollection<PersonContact> PersonContactCsRelationships { get; set; } = new List<PersonContact>();
}
