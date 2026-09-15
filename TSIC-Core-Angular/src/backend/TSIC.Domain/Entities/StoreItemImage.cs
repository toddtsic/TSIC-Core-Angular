using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreItemImage
{
    public int StoreItemImageId { get; set; }

    public int StoreItemId { get; set; }

    public string ImageUrl { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public string? AltText { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual StoreItems StoreItem { get; set; } = null!;
}
