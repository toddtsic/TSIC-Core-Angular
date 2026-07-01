using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TemplateGames
{
    public int TemplateGameId { get; set; }

    public int TemplateId { get; set; }

    public string RoundType { get; set; } = null!;

    public int GameKey { get; set; }

    public int? Slot1Seed { get; set; }

    public int? Slot2Seed { get; set; }

    public int SortOrder { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public bool IsOptional { get; set; }

    public virtual ICollection<AdvancementRoutes> AdvancementRoutesSourceTemplateGame { get; set; } = new List<AdvancementRoutes>();

    public virtual ICollection<AdvancementRoutes> AdvancementRoutesTargetTemplateGame { get; set; } = new List<AdvancementRoutes>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Templates Template { get; set; } = null!;
}
