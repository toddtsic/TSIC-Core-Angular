using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PersistedGrant
{
    public string Key { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string? SubjectId { get; set; }

    public string ClientId { get; set; } = null!;

    public DateTime CreationTime { get; set; }

    public DateTime? Expiration { get; set; }

    public string Data { get; set; } = null!;
}
