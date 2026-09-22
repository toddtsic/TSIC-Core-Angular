using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Messages
{
    public Guid MessageId { get; set; }

    public Guid TeamId { get; set; }

    public Guid JobId { get; set; }

    public long Seq { get; set; }

    public long LastTouchSeq { get; set; }

    public byte Kind { get; set; }

    public string Message { get; set; } = null!;

    public string CreatorUserId { get; set; } = null!;

    public Guid RegId { get; set; }

    public Guid ClientMessageId { get; set; }

    public Guid? ReplyToMessageId { get; set; }

    public DateTime Created { get; set; }

    public long? EditedSeq { get; set; }

    public DateTime? EditedAt { get; set; }

    public long? DeletedSeq { get; set; }

    public string? DeletedByUserId { get; set; }

    public DateTime? PinnedAt { get; set; }

    public string? PinnedByUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual ICollection<Attachments> Attachments { get; set; } = new List<Attachments>();

    public virtual AspNetUsers CreatorUser { get; set; } = null!;

    public virtual AspNetUsers? DeletedByUser { get; set; }

    public virtual ICollection<Messages> InverseReplyToMessage { get; set; } = new List<Messages>();

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<Mentions> Mentions { get; set; } = new List<Mentions>();

    public virtual ICollection<MessageReports> MessageReports { get; set; } = new List<MessageReports>();

    public virtual ICollection<MessageRevisions> MessageRevisions { get; set; } = new List<MessageRevisions>();

    public virtual AspNetUsers? PinnedByUser { get; set; }

    public virtual ICollection<Reactions> Reactions { get; set; } = new List<Reactions>();

    public virtual Registrations Reg { get; set; } = null!;

    public virtual Messages? ReplyToMessage { get; set; }

    public virtual Teams Team { get; set; } = null!;
}
