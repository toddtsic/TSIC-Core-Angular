using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientPostLogoutRedirectUri
{
    public int Id { get; set; }

    public string PostLogoutRedirectUri { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Client Client { get; set; } = null!;
}
