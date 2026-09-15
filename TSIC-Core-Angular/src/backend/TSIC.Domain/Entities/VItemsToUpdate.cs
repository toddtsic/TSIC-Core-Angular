using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class VItemsToUpdate
{
    public string StoreItemName { get; set; } = null!;

    public decimal StoreItemPrice { get; set; }

    public int StoreId { get; set; }
}
