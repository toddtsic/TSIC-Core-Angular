using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobDiscountCode
{
    public int Ai { get; set; }

    public Guid JobId { get; set; }

    public string CodeName { get; set; } = null!;

    public bool BAsPercent { get; set; }

    public decimal? CodeAmount { get; set; }

    public bool Active { get; set; }

    public DateTime CodeStartDate { get; set; }

    public DateTime CodeEndDate { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual ICollection<RegistrationAccounting> RegistrationAccountings { get; set; } = new List<RegistrationAccounting>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccountings { get; set; } = new List<StoreCartBatchAccounting>();
}
