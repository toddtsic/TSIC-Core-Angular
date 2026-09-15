using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AgegroupWaveAssignment
{
    public Guid AgegroupId { get; set; }

    public DateTime GameDate { get; set; }

    public byte Wave { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Agegroups Agegroup { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
