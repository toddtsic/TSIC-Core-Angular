using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AdvancementRoutes
{
    public int AdvancementRouteId { get; set; }

    public int SourceTemplateGameId { get; set; }

    public string SourceResult { get; set; } = null!;

    public int TargetTemplateGameId { get; set; }

    public byte TargetSlot { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual TemplateGames SourceTemplateGame { get; set; } = null!;

    public virtual TemplateGames TargetTemplateGame { get; set; } = null!;
}
