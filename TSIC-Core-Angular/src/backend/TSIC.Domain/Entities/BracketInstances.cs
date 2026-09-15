using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class BracketInstances
{
    public int BracketInstanceId { get; set; }

    public Guid JobId { get; set; }

    public Guid AgegroupId { get; set; }

    public Guid? DivId { get; set; }

    public int TemplateId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual ICollection<AdvancementFeeds> AdvancementFeeds { get; set; } = new List<AdvancementFeeds>();

    public virtual Agegroups Agegroup { get; set; } = null!;

    public virtual Divisions? Div { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<SeedAssignments> SeedAssignments { get; set; } = new List<SeedAssignments>();

    public virtual Templates Template { get; set; } = null!;
}
