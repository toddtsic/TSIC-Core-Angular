using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamMessages
{
    public Guid Id { get; set; }

    public Guid SenderRegistrationId { get; set; }

    public bool BClubBroadcast { get; set; }

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? AttachmentUrl { get; set; }

    public string? PhotoUrl { get; set; }

    public DateTime Createdate { get; set; }

    public DateTime Modified { get; set; }

    public int DaysVisible { get; set; }

    public Guid? TeamId { get; set; }

    public virtual Registrations SenderRegistration { get; set; } = null!;
}
