using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientIdPrestriction
{
    public int Id { get; set; }

    public string Provider { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Client Client { get; set; } = null!;
}
