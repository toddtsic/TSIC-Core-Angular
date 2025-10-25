using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientScope
{
    public int Id { get; set; }

    public string Scope { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Client Client { get; set; } = null!;
}
