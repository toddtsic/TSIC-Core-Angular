using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Sport
{
    public string? ImageUrl { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public Guid SportId { get; set; }

    public string? SportName { get; set; }

    public int Ai { get; set; }

    public virtual ICollection<Club> Clubs { get; set; } = new List<Club>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<League> Leagues { get; set; } = new List<League>();

    public virtual AspNetUser? LebUser { get; set; }
}
