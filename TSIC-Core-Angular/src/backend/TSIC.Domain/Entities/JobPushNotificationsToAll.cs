using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobPushNotificationsToAll
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string PushText { get; set; } = null!;

    public int DeviceCount { get; set; }

    public Guid? TeamId { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Teams? Team { get; set; }
}
