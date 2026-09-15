using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientRedirectUris
{
    public int Id { get; set; }

    public string RedirectUri { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Clients Client { get; set; } = null!;
}
