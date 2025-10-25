using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class OpenIddictScopes
{
    public string Id { get; set; } = null!;

    public string? Description { get; set; }
}
