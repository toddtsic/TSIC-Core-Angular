using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Customers
{
    public int TzId { get; set; }

    public string? AdnLoginId { get; set; }

    public string? AdnTransactionKey { get; set; }

    public int CustomerAi { get; set; }

    public Guid CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? Theme { get; set; }

    public virtual ICollection<CustomerGroupCustomers> CustomerGroupCustomers { get; set; } = new List<CustomerGroupCustomers>();

    public virtual ICollection<JobCustomers> JobCustomers { get; set; } = new List<JobCustomers>();

    public virtual ICollection<Jobs> Jobs { get; set; } = new List<Jobs>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();

    public virtual Themes? ThemeNavigation { get; set; }

    public virtual Timezones Tz { get; set; } = null!;
}
