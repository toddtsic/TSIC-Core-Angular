using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DeviceCodes
{
    public string DeviceCode { get; set; } = null!;

    public string UserCode { get; set; } = null!;

    public string? SubjectId { get; set; }

    public string ClientId { get; set; } = null!;

    public DateTime CreationTime { get; set; }

    public DateTime Expiration { get; set; }

    public string Data { get; set; } = null!;
}
