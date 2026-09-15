using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClientProperties
{
    public int Id { get; set; }

    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public int ClientId { get; set; }

    public virtual Clients Client { get; set; } = null!;
}
