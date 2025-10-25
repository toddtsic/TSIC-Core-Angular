using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientIdPrestrictions
{
    public int Id { get; set; }

    public string Provider { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Clients Client { get; set; } = null!;
}
