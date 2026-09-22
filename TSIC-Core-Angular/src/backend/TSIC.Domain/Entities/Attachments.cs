using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Attachments
{
    public Guid AttachmentId { get; set; }

    public Guid MessageId { get; set; }

    public byte Kind { get; set; }

    public string? OriginalFileName { get; set; }

    public string ContentType { get; set; } = null!;

    public long ByteSize { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int SortOrder { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Messages Message { get; set; } = null!;
}
