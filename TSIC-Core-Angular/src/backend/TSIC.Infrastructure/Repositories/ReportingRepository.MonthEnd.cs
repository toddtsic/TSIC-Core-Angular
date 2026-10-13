using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Repositories;

// The month-end close: EF replacement for the data loads of adn.MonthyQBPExport_Automated, the
// five procedures it runs for its sheets, and the calculation in the one that fills
// adn.Monthly_Job_Stats. Read-only.
public partial class ReportingRepository
{
    // The second role the month-end statistics count as a player.
    private const string UnassignedPlayerRoleId = "15411BA2-96D7-482D-8745-40B123EE4687";

    public async Task<MonthEndRegistrationDataDto> GetMonthEndRegistrationDataAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        // The month's gateway transactions, matched on the settlement TEXT
        // ("13-Sep-2026 04:12:31 PM EDT"). The procedures match it two ways — the month and year
        // at fixed positions, and "Sep-2026" anywhere — so a row matching either is read and the
        // service applies each rule where its procedure did.
        var yearText = year.ToString(CultureInfo.InvariantCulture);
        var monthText = new DateTime(year, month, 1).ToString("MMM", CultureInfo.InvariantCulture);
        var marker = monthText + "-" + yearText;
        var transactions = await _context.Txs.AsNoTracking()
            .Where(t => t.SettlementDateTime != null
                && ((t.SettlementDateTime.Substring(7, 4) == yearText && t.SettlementDateTime.Substring(3, 3) == monthText)
                    || t.SettlementDateTime.Contains(marker)))
            .Select(t => new MonthEndTransactionDto
            {
                TransactionId = t.TransactionId,
                TransactionStatus = t.TransactionStatus,
                SettlementDateTime = t.SettlementDateTime,
                SettlementAmount = t.SettlementAmount,
                InvoiceNumber = t.InvoiceNumber,
                TransactionType = t.TransactionType,
            })
            .ToListAsync(cancellationToken);

        // Every accounting record on those transactions. The id list is applied to the gateway
        // table's key and the accounting records joined from there (see
        // GetMonthlyCustomerRollupAsync). The id returned is the gateway's own spelling of it.
        var txIds = transactions.Select(t => t.TransactionId).ToList();
        var accountingRecords = await (
            from t in _context.Txs.AsNoTracking()
            where txIds.Contains(t.TransactionId)
            join ra in _context.RegistrationAccounting on t.TransactionId equals ra.AdnTransactionId
            join apm0 in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm0.PaymentMethodId into apms
            from apm in apms.DefaultIfEmpty()
            join r0 in _context.Registrations on ra.RegistrationId equals (Guid?)r0.RegistrationId into rs
            from r in rs.DefaultIfEmpty()
            select new MonthEndAccountingRecordDto
            {
                TransactionId = t.TransactionId,
                AccountingId = ra.AId,
                PaymentMethod = apm != null ? apm.PaymentMethod : null,
                RegistrationJobId = r != null ? (Guid?)r.JobId : null,
            })
            .ToListAsync(cancellationToken);

        var adminCharges = await _context.JobAdminCharges.AsNoTracking()
            .Where(jac => jac.Year == year && jac.Month == month)
            .OrderBy(jac => jac.Id)
            .Select(jac => new MonthEndAdminChargeDto
            {
                Id = jac.Id,
                JobId = jac.JobId,
                ChargeTypeId = jac.ChargeTypeId,
                ChargeAmount = jac.ChargeAmount,
                Comment = jac.Comment,
                CreateDate = jac.CreateDate,
            })
            .ToListAsync(cancellationToken);

        return new MonthEndRegistrationDataDto
        {
            Transactions = transactions,
            AccountingRecords = accountingRecords,
            Jobs = await LoadMonthEndJobsAsync(cancellationToken),
            AdminCharges = adminCharges,
        };
    }

    public async Task<MonthEndMerchandiseDataDto> GetMonthEndMerchandiseDataAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the data load of adn.MonthyQBPExport_Automated_Merch: the month's
        // gateway transactions whose invoice number marks them as merchandise ("…_M"), and the
        // store payment records carrying their ids.
        var marker = new DateTime(year, month, 1).ToString("MMM", CultureInfo.InvariantCulture)
            + "-" + year.ToString(CultureInfo.InvariantCulture);
        var transactions = await _context.Txs.AsNoTracking()
            .Where(t => t.SettlementDateTime != null && t.SettlementDateTime.Contains(marker)
                && t.InvoiceNumber != null && t.InvoiceNumber.Contains("_M"))
            .Select(t => new MonthEndTransactionDto
            {
                TransactionId = t.TransactionId,
                TransactionStatus = t.TransactionStatus,
                SettlementDateTime = t.SettlementDateTime,
                SettlementAmount = t.SettlementAmount,
                InvoiceNumber = t.InvoiceNumber,
                TransactionType = t.TransactionType,
            })
            .ToListAsync(cancellationToken);

        // Every payment record on those transactions, with the job its store belongs to. The
        // procedure reaches the job through the cart batch, the cart, the store and the payment
        // method, all of which must exist; a record missing any of them is flagged, not dropped,
        // because the procedure's sheets join the payment records a second time on the id alone.
        var txIds = transactions.Select(t => t.TransactionId).ToList();
        var payments = await (
            from t in _context.Txs.AsNoTracking()
            where txIds.Contains(t.TransactionId)
            join scba in _context.StoreCartBatchAccounting on t.TransactionId equals scba.AdnTransactionId
            join scb0 in _context.StoreCartBatches on scba.StoreCartBatchId equals scb0.StoreCartBatchId into scbs
            from scb in scbs.DefaultIfEmpty()
            join sc0 in _context.StoreCart on scb.StoreCartId equals sc0.StoreCartId into scs
            from sc in scs.DefaultIfEmpty()
            join s0 in _context.Stores on sc.StoreId equals s0.StoreId into ss
            from s in ss.DefaultIfEmpty()
            join apm0 in _context.AccountingPaymentMethods on scba.PaymentMethodId equals apm0.PaymentMethodId into apms
            from apm in apms.DefaultIfEmpty()
            join j0 in _context.Jobs on s.JobId equals j0.JobId into js
            from j in js.DefaultIfEmpty()
            join c0 in _context.Customers on j.CustomerId equals c0.CustomerId into cs
            from c in cs.DefaultIfEmpty()
            select new
            {
                TransactionId = t.TransactionId,
                scba.StoreCartBatchAccountingId,
                scba.StoreCartBatchId,
                Resolved = scb != null && sc != null && s != null && apm != null && j != null && c != null,
                PaymentMethod = apm != null ? apm.PaymentMethod : null,
                JobId = j != null ? (Guid?)j.JobId : null,
            })
            .ToListAsync(cancellationToken);

        // The product fees of each cart batch, every line counted.
        var batchIds = payments.Select(p => p.StoreCartBatchId).Distinct().ToList();
        var productFees = await _context.StoreCartBatchSkus.AsNoTracking()
            .Where(k => batchIds.Contains(k.StoreCartBatchId))
            .GroupBy(k => k.StoreCartBatchId)
            .Select(g => new { StoreCartBatchId = g.Key, Fees = g.Sum(k => k.FeeProduct) })
            .ToDictionaryAsync(x => x.StoreCartBatchId, x => x.Fees, cancellationToken);

        return new MonthEndMerchandiseDataDto
        {
            Transactions = transactions,
            Payments = payments
                .Select(p => new MonthEndStorePaymentDto
                {
                    TransactionId = p.TransactionId,
                    AccountingId = p.StoreCartBatchAccountingId,
                    Resolved = p.Resolved,
                    PaymentMethod = p.PaymentMethod,
                    JobId = p.JobId,
                    BatchProductFees = productFees.TryGetValue(p.StoreCartBatchId, out var fees) ? fees : null,
                })
                .ToList(),
            Jobs = await LoadMonthEndJobsAsync(cancellationToken),
        };
    }

    /// <summary>Every job with its customer, type, rates and name ranks.</summary>
    private async Task<List<MonthEndJobDto>> LoadMonthEndJobsAsync(CancellationToken cancellationToken)
    {
        var jobs = await (
            from j in _context.Jobs.AsNoTracking()
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join jt0 in _context.JobTypes on j.JobTypeId equals jt0.JobTypeId into jts
            from jt in jts.DefaultIfEmpty()
            select new
            {
                j.JobId,
                j.JobAi,
                j.CustomerId,
                c.CustomerName,
                SettlesThroughTsic = c.AdnLoginId == TsicGatewayLoginId,
                j.JobName,
                j.JobNameQbp,
                HasJobType = jt != null,
                j.JobTypeId,
                JobTypeDesc = jt != null ? jt.JobTypeDesc : null,
                j.PerPlayerCharge,
                j.PerTeamCharge,
                j.ProcessingFeePercent,
                j.EcprocessingFeePercent,
                j.StoreTsicrate,
            })
            .ToListAsync(cancellationToken);

        // Names are sorted BY THE DATABASE, under whose collation the procedures order their
        // sheets, and handed on as ranks.
        var customerNameOrder = RankInDatabaseOrder(await _context.Customers.AsNoTracking()
            .OrderBy(c => c.CustomerName)
            .Select(c => new NamedKey<Guid> { Key = c.CustomerId, Name = c.CustomerName })
            .ToListAsync(cancellationToken));
        var jobNameOrder = RankInDatabaseOrder(await _context.Jobs.AsNoTracking()
            .OrderBy(j => j.JobName)
            .Select(j => new NamedKey<Guid> { Key = j.JobId, Name = j.JobName })
            .ToListAsync(cancellationToken));
        var reportedNameOrder = RankInDatabaseOrder(await _context.Jobs.AsNoTracking()
            .OrderBy(j => j.JobNameQbp ?? j.JobName)
            .Select(j => new NamedKey<Guid> { Key = j.JobId, Name = j.JobNameQbp ?? j.JobName })
            .ToListAsync(cancellationToken));

        var groupMemberships = await (
            from cgc in _context.CustomerGroupCustomers.AsNoTracking()
            join cg in _context.CustomerGroups on cgc.CustomerGroupId equals cg.Id
            orderby cgc.Id
            select new { cgc.CustomerId, cg.CustomerGroupName })
            .ToListAsync(cancellationToken);
        var groupNamesByCustomer = groupMemberships.ToLookup(g => g.CustomerId, g => g.CustomerGroupName);

        return jobs
            .Select(j => new MonthEndJobDto
            {
                JobId = j.JobId,
                JobAi = j.JobAi,
                CustomerId = j.CustomerId,
                CustomerName = j.CustomerName,
                CustomerGroupNames = groupNamesByCustomer[j.CustomerId].ToList(),
                SettlesThroughTsic = j.SettlesThroughTsic,
                JobName = j.JobName,
                JobNameQbp = j.JobNameQbp,
                HasJobType = j.HasJobType,
                JobTypeId = j.JobTypeId,
                JobTypeDesc = j.JobTypeDesc,
                PerPlayerCharge = j.PerPlayerCharge,
                PerTeamCharge = j.PerTeamCharge,
                ProcessingFeePercent = j.ProcessingFeePercent,
                EcheckProcessingFeePercent = j.EcprocessingFeePercent,
                StoreTsicRate = j.StoreTsicrate,
                CustomerNameOrder = customerNameOrder[j.CustomerId],
                JobNameOrder = jobNameOrder[j.JobId],
                ReportedNameOrder = reportedNameOrder[j.JobId],
            })
            .ToList();
    }

    private sealed class NamedKey<TKey>
    {
        public required TKey Key { get; init; }
        public required string? Name { get; init; }
    }

    /// <summary>
    /// Ranks for rows the database has already sorted by name: a row whose name the database
    /// treats as equal to the one before it (case and trailing spaces aside) shares its rank.
    /// </summary>
    private static Dictionary<TKey, int> RankInDatabaseOrder<TKey>(List<NamedKey<TKey>> sorted)
        where TKey : notnull
    {
        var ranks = new Dictionary<TKey, int>();
        var rank = -1;
        string? previous = null;
        for (var i = 0; i < sorted.Count; i++)
        {
            var name = sorted[i].Name?.TrimEnd(' ').ToUpperInvariant();
            if (i == 0 || name != previous)
            {
                rank++;
            }

            previous = name;
            ranks[sorted[i].Key] = rank;
        }

        return ranks;
    }

    public async Task<List<MonthEndJobStatDto>> GetMonthlyJobStatsAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        return await _context.MonthlyJobStats.AsNoTracking()
            .Where(m => m.Year == year && m.Month == month)
            .OrderBy(m => m.Aid)
            .Select(m => new MonthEndJobStatDto
            {
                Aid = m.Aid,
                JobId = m.JobId,
                ActivePlayersToDateLastMonth = m.CountActivePlayersToDateLastMonth,
                ActivePlayersToDate = m.CountActivePlayersToDate,
                NewPlayersThisMonth = m.CountNewPlayersThisMonth,
                ActiveTeamsToDateLastMonth = m.CountActiveTeamsToDateLastMonth,
                ActiveTeamsToDate = m.CountActiveTeamsToDate,
                NewTeamsThisMonth = m.CountNewTeamsThisMonth,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<MonthEndJobStatDto>> ComputeMonthlyJobStatsAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the calculation in adn.UpdateMonthlyJobStats_CalcFields: one row per
        // job with player or team activity in the month, whether or not the month already has a
        // row for it. Nothing is stored here.
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        // Transactions the gateway settled in the month. The procedure's window is "between the
        // 1st and the next 1st", which also takes one settled at midnight exactly on the next 1st.
        var yearText = year.ToString(CultureInfo.InvariantCulture);
        var monthText = start.ToString("MMM", CultureInfo.InvariantCulture);
        var nextYearText = end.Year.ToString(CultureInfo.InvariantCulture);
        var nextMonthText = end.ToString("MMM", CultureInfo.InvariantCulture);
        var txIds = await _context.Txs.AsNoTracking()
            .Where(t => t.SettlementDateTime != null
                && ((t.SettlementDateTime.Substring(7, 4) == yearText && t.SettlementDateTime.Substring(3, 3) == monthText)
                    || (t.SettlementDateTime.Substring(7, 4) == nextYearText
                        && t.SettlementDateTime.Substring(3, 3) == nextMonthText
                        && t.SettlementDateTime.Substring(0, 2) == "01"
                        && t.SettlementDateTime.Substring(11, 12) == " 12:00:00 AM")))
            .Select(t => t.TransactionId)
            .ToListAsync(cancellationToken);

        // Jobs with PLAYER activity: an active, fee-bearing player registered in the month; or
        // an active accounting record entered in the month on a player; or ANY accounting record
        // (active or not) on a player whose transaction settled in the month. The last leg
        // ignoring "active" is the procedure's own precedence (AND binds tighter than OR).
        var playerJobs = new HashSet<Guid>(await _context.Registrations.AsNoTracking()
            .Where(r => r.BActive == true && r.FeeBase > 0 && r.RegistrationTs >= start && r.RegistrationTs <= end
                && (r.RoleId == RoleConstants.Player || r.RoleId == UnassignedPlayerRoleId))
            .Select(r => r.JobId)
            .Distinct()
            .ToListAsync(cancellationToken));
        playerJobs.UnionWith(await (
            from ra in _context.RegistrationAccounting.AsNoTracking()
            where ra.Active == true && ra.Createdate >= start && ra.Createdate <= end
            join r in _context.Registrations on ra.RegistrationId equals (Guid?)r.RegistrationId
            where r.RoleId == RoleConstants.Player || r.RoleId == UnassignedPlayerRoleId
            select r.JobId)
            .Distinct()
            .ToListAsync(cancellationToken));
        playerJobs.UnionWith(await (
            from t in _context.Txs.AsNoTracking()
            where txIds.Contains(t.TransactionId)
            join ra in _context.RegistrationAccounting on t.TransactionId equals ra.AdnTransactionId
            join r in _context.Registrations on ra.RegistrationId equals (Guid?)r.RegistrationId
            where r.RoleId == RoleConstants.Player || r.RoleId == UnassignedPlayerRoleId
            select r.JobId)
            .Distinct()
            .ToListAsync(cancellationToken));

        // Jobs with TEAM activity: an active, fee-bearing team created in the month; or an active
        // accounting record on a team, entered in the month or settled in it.
        var teamJobs = new HashSet<Guid>(await _context.Teams.AsNoTracking()
            .Where(t => t.Active == true && t.FeeBase > 0 && t.Createdate >= start && t.Createdate <= end)
            .Select(t => t.JobId)
            .Distinct()
            .ToListAsync(cancellationToken));
        teamJobs.UnionWith(await (
            from ra in _context.RegistrationAccounting.AsNoTracking()
            where ra.Active == true && ra.Createdate >= start && ra.Createdate <= end
            join t in _context.Teams on ra.TeamId equals (Guid?)t.TeamId
            select t.JobId)
            .Distinct()
            .ToListAsync(cancellationToken));
        teamJobs.UnionWith(await (
            from tx in _context.Txs.AsNoTracking()
            where txIds.Contains(tx.TransactionId)
            join ra in _context.RegistrationAccounting on tx.TransactionId equals ra.AdnTransactionId
            where ra.Active == true
            join t in _context.Teams on ra.TeamId equals (Guid?)t.TeamId
            select t.JobId)
            .Distinct()
            .ToListAsync(cancellationToken));

        var jobIds = playerJobs.Union(teamJobs).ToList();

        // Billable players and teams to date, as of the end of the month.
        var playersToDate = await _context.Registrations.AsNoTracking()
            .Where(r => jobIds.Contains(r.JobId) && r.RegistrationTs < end && r.BActive == true && r.FeeBase > 0
                && (r.RoleId == RoleConstants.Player || r.RoleId == UnassignedPlayerRoleId))
            .GroupBy(r => r.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobId, x => x.Count, cancellationToken);
        var teamsToDate = await (
            from t in _context.Teams.AsNoTracking()
            join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
            where jobIds.Contains(t.JobId) && t.Createdate < end && t.Active == true && t.FeeBase > 0
            group t by t.JobId into g
            select new { JobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobId, x => x.Count, cancellationToken);

        // Each job's rows for earlier months: "last month's" figures come from one of them.
        var earlier = await _context.MonthlyJobStats.AsNoTracking()
            .Where(m => jobIds.Contains(m.JobId) && (m.Year < year || (m.Year == year && m.Month < month)))
            .Select(m => new { m.JobId, m.Aid, m.CountActivePlayersToDate, m.CountActiveTeamsToDate })
            .ToListAsync(cancellationToken);
        var earlierByJob = earlier.ToLookup(m => m.JobId);

        var rows = new List<MonthEndJobStatDto>();
        foreach (var jobId in jobIds)
        {
            var hasPlayers = playerJobs.Contains(jobId);

            // The row "last month" is read from is the job's LATEST-WRITTEN earlier row (highest
            // id, not latest month): for players the latest with any players to date; for teams
            // the latest of all — or, for a job here on player activity alone, the players' row.
            var playerSource = earlierByJob[jobId].Where(m => (m.CountActivePlayersToDate ?? 0) > 0).MaxBy(m => m.Aid);
            var teamSource = teamJobs.Contains(jobId) ? earlierByJob[jobId].MaxBy(m => m.Aid) : playerSource;

            var players = hasPlayers ? playersToDate.GetValueOrDefault(jobId) : 0;
            var playersLastMonth = hasPlayers ? playerSource?.CountActivePlayersToDate ?? 0 : 0;
            var teams = teamsToDate.GetValueOrDefault(jobId);
            var teamsLastMonth = teamSource?.CountActiveTeamsToDate ?? 0;

            rows.Add(new MonthEndJobStatDto
            {
                Aid = null,
                JobId = jobId,
                ActivePlayersToDateLastMonth = playersLastMonth,
                ActivePlayersToDate = players,
                NewPlayersThisMonth = players > 0 ? players - playersLastMonth : players,
                ActiveTeamsToDateLastMonth = teamsLastMonth,
                ActiveTeamsToDate = teams,
                NewTeamsThisMonth = teams > 0 ? teams - teamsLastMonth : teams,
            });
        }

        return rows;
    }

    public async Task<int> AddMissingMonthlyJobStatsAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the write in adn.UpdateMonthlyJobStats_CalcFields: a job that already
        // has a row for the month keeps it untouched; every other job with activity gets one.
        var computed = await ComputeMonthlyJobStatsAsync(year, month, cancellationToken);
        var stored = await _context.MonthlyJobStats.AsNoTracking()
            .Where(m => m.Year == year && m.Month == month)
            .Select(m => m.JobId)
            .ToListAsync(cancellationToken);
        var missing = computed.Where(c => !stored.Contains(c.JobId)).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        // Rows are stamped with the system account, as the procedure stamps them.
        var superUserId = await _context.AspNetUsers.AsNoTracking()
            .Where(u => u.UserName == "TSICSuperUser")
            .Select(u => u.Id)
            .SingleAsync(cancellationToken);
        var now = DateTime.Now;

        _context.MonthlyJobStats.AddRange(missing.Select(c => new MonthlyJobStats
        {
            JobId = c.JobId,
            Year = year,
            Month = month,
            CountActivePlayersToDateLastMonth = c.ActivePlayersToDateLastMonth,
            CountActivePlayersToDate = c.ActivePlayersToDate,
            CountNewPlayersThisMonth = c.NewPlayersThisMonth,
            CountActiveTeamsToDateLastMonth = c.ActiveTeamsToDateLastMonth,
            CountActiveTeamsToDate = c.ActiveTeamsToDate,
            CountNewTeamsThisMonth = c.NewTeamsThisMonth,
            LebUserId = superUserId,
            Modified = now,
        }));
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
