using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientSecrets
{
    public int Id { get; set; }

    public string? Description { get; set; }

    public string Value { get; set; } = null!;

    public DateTime? Expiration { get; set; }

    public string Type { get; set; } = null!;

    public DateTime Created { get; set; }

    public int ClientId { get; set; }

    public virtual Clients Client { get; set; } = null!;
}
