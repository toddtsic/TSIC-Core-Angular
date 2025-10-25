using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Jobinvoice
{
    public int Ai { get; set; }

    public Guid? JobId { get; set; }

    public int? Year { get; set; }

    public int? Month { get; set; }

    public bool? Active { get; set; }

    public virtual Job? Job { get; set; }
}
