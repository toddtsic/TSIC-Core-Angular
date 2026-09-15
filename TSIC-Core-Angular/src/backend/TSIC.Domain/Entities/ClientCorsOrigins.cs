using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientCorsOrigins
{
    public int Id { get; set; }

    public string Origin { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Clients Client { get; set; } = null!;
}
