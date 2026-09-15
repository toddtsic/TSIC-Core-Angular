using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ZzAdultConfirmTemplatesBackup20260814
{
    public Guid JobId { get; set; }

    public string? JobName { get; set; }

    public int JobTypeId { get; set; }

    public DateTime ExpiryAdmin { get; set; }

    public string? AdultRegConfirmationEmail { get; set; }

    public string? AdultRegConfirmationOnScreen { get; set; }

    public DateTime BackupTs { get; set; }
}
