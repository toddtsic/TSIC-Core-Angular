using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Customer
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

    public virtual ICollection<CustomerGroupCustomer> CustomerGroupCustomers { get; set; } = new List<CustomerGroupCustomer>();

    public virtual ICollection<JobCustomer> JobCustomers { get; set; } = new List<JobCustomer>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual Theme? ThemeNavigation { get; set; }

    public virtual Timezone Tz { get; set; } = null!;
}
