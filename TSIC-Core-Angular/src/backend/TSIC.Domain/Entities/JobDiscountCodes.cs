using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobDiscountCodes
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

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual ICollection<RegistrationAccounting> RegistrationAccounting { get; set; } = new List<RegistrationAccounting>();

    public virtual ICollection<Registrations> Registrations { get; set; } = new List<Registrations>();

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccounting { get; set; } = new List<StoreCartBatchAccounting>();
}
