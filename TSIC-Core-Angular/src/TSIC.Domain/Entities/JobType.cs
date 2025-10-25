using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobType
{
    public string? JobTypeDesc { get; set; }

    public int JobTypeId { get; set; }

    public string? JobTypeName { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
