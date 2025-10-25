using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobMessage
{
    public Guid Id { get; set; }

    public Guid SenderRegistrationId { get; set; }

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? AttachmentUrl { get; set; }

    public string? PhotoUrl { get; set; }

    public DateTime Createdate { get; set; }

    public DateTime Modified { get; set; }

    public int DaysVisible { get; set; }

    public Guid? JobId { get; set; }

    public string? RoleId { get; set; }

    public virtual Job? Job { get; set; }

    public virtual AspNetRole? Role { get; set; }

    public virtual Registration SenderRegistration { get; set; } = null!;
}
