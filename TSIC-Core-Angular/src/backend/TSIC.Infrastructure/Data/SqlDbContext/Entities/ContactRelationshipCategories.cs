using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ContactRelationshipCategories
{
    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? Relationship { get; set; }

    public Guid RelationshipId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<PersonContacts> PersonContactsCeRelationship { get; set; } = new List<PersonContacts>();

    public virtual ICollection<PersonContacts> PersonContactsCpRelationship { get; set; } = new List<PersonContacts>();

    public virtual ICollection<PersonContacts> PersonContactsCsRelationship { get; set; } = new List<PersonContacts>();
}
