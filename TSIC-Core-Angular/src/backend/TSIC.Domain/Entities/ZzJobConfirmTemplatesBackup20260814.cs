using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ZzJobConfirmTemplatesBackup20260814
{
    public Guid JobId { get; set; }

    public string? JobName { get; set; }

    public int JobTypeId { get; set; }

    public DateTime ExpiryAdmin { get; set; }

    public string? CoachRegConfirmationEmail { get; set; }

    public string? CoachRegConfirmationOnScreen { get; set; }

    public string? RefereeRegConfirmationEmail { get; set; }

    public string? RefereeRegConfirmationOnScreen { get; set; }

    public string? RecruiterRegConfirmationEmail { get; set; }

    public string? RecruiterRegConfirmationOnScreen { get; set; }

    public DateTime BackupTs { get; set; }
}
