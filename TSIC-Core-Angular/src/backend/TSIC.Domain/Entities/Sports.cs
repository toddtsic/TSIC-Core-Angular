using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Sports
{
    public string? ImageUrl { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public Guid SportId { get; set; }

    public string? SportName { get; set; }

    public int Ai { get; set; }

    public virtual ICollection<Clubs> Clubs { get; set; } = new List<Clubs>();

    public virtual ICollection<Jobs> Jobs { get; set; } = new List<Jobs>();

    public virtual ICollection<Leagues> Leagues { get; set; } = new List<Leagues>();

    public virtual AspNetUsers? LebUser { get; set; }
}
