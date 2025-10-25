using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientScopes
{
    public int Id { get; set; }

    public string Scope { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Clients Client { get; set; } = null!;
}
