using System;
using System.Collections.Generic;

namespace TSIC.Domain.LogEntities;

public partial class AppUsage
{
    public long Id { get; set; }

    public DateTime OccurredAt { get; set; }

    public int AppClientId { get; set; }

    public int PlatformId { get; set; }

    public string AppVersion { get; set; } = null!;

    public string Controller { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string? QueryString { get; set; }

    public short StatusCode { get; set; }

    public string? UserId { get; set; }

    public Guid? RegId { get; set; }

    public Guid JobId { get; set; }

    public Guid? TeamId { get; set; }

    public bool IsBot { get; set; }

    public int BrowserId { get; set; }

    public int DeviceClassId { get; set; }

    public virtual AppClients AppClient { get; set; } = null!;

    public virtual Browsers Browser { get; set; } = null!;

    public virtual DeviceClasses DeviceClass { get; set; } = null!;

    public virtual Platforms Platform { get; set; } = null!;
}
