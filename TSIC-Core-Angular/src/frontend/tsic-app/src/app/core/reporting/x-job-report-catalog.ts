/**
 * X-Job (cross-job / platform-wide) report catalogue — hard-coded, SuperUser only.
 *
 * These are TSIC-internal reports that aggregate across ALL jobs, so they have no
 * per-job rows in `reporting.JobReports` (the per-job library's source). They are
 * the new-app home for the legacy tsic home job's SuperUser "Reports" menu group
 * plus a mirror of the SuperUser "Accounting" nav group — one page for everything
 * cross-job.
 *
 * Dispatch by kind:
 *   - 'endpoint' → direct ReportingController GET (EF+Syncfusion PDF renders)
 *   - 'sp'       → reporting/export-sp Excel dump, always bUseJobId=false.
 *                  Authorized server-side by GlobalSuperuserReports (fail-closed
 *                  allow-list) — when adding an 'sp' entry here, add its spName
 *                  there in the same change.
 *   - 'route'    → in-app tool, jobPath-relative router navigation
 *
 * spName format: bracketed `[schema].[name]` where legacy used it (required for
 * names with hyphens — CommandType.StoredProcedure needs delimited identifiers);
 * the backend allow-list compares bracket-insensitively.
 */

export type XJobEntryKind = 'endpoint' | 'sp' | 'route';

export interface XJobReportEntry {
    id: string;
    title: string;
    description: string;
    iconName: string;
    kind: XJobEntryKind;
    /** kind='endpoint': ReportingController route name */
    endpointPath?: string;
    /** kind='sp': stored proc for reporting/export-sp (bUseJobId=false) */
    spName?: string;
    /** kind='route': jobPath-relative in-app route */
    route?: string;
}

export interface XJobReportSection {
    title: string;
    iconName: string;
    entries: readonly XJobReportEntry[];
}

export const X_JOB_REPORT_SECTIONS: readonly XJobReportSection[] = [
    {
        title: 'Cross-Job Reports',
        iconName: 'globe',
        entries: [
            { id: 'xj-daily-reg-counts',        title: 'Daily Registration Counts (pdf)',                description: 'Registrations per day, broken out across every active job',        iconName: 'calendar-check',           kind: 'endpoint', endpointPath: 'Get_JobPlayers_TSICDAILY' },
            { id: 'xj-regsaver-purchases',      title: 'Regsaver Purchases (Excel)',                     description: 'RegSaver policy purchases across all jobs',                        iconName: 'shield-check',             kind: 'sp', spName: '[reporting].[RegsaverRegistrants_ALL]' },
            { id: 'xj-regsaver-rawdata',        title: 'Regsaver Purchases — Raw Data (Excel)',          description: 'Raw RegSaver purchase rows across all jobs',                       iconName: 'file-earmark-spreadsheet', kind: 'sp', spName: '[reporting].[RegsaverPurchases_ALL_Rawdata]' },
            { id: 'xj-expired-player-bulletins', title: 'Expired Player Reg Bulletins on Active Sites',  description: 'QA sweep: active sites whose player-registration bulletins have expired', iconName: 'exclamation-triangle', kind: 'sp', spName: '[utility].[PlayerRegistrationBulletinsQA]' },
            { id: 'xj-expired-team-bulletins',  title: 'Expired Team Reg Bulletins on Active Sites',     description: 'QA sweep: active sites whose team-registration bulletins have expired',   iconName: 'exclamation-triangle', kind: 'sp', spName: '[utility].[TeamRegistrationBulletinsQA]' },
            { id: 'xj-suspicious-arbs',         title: 'List of Suspicious ARBs',                        description: 'ARB subscriptions flagged for review across all jobs',             iconName: 'shield-exclamation',       kind: 'sp', spName: '[utility].[GetSuspiciousArbs]' },
            { id: 'xj-job-key-attributes',      title: 'Job Key Attributes — ALL',                       description: 'Key configuration attributes for every job',                       iconName: 'key',                      kind: 'sp', spName: '[reporting].[JobKeyAttributes-ALL]' },
            { id: 'xj-clubrep-contacts',        title: 'Club Rep Contacts — ALL',                        description: 'Club rep contact list across all jobs',                            iconName: 'person-lines-fill',        kind: 'sp', spName: '[reporting].[ClubRepContacts-All]' },
            { id: 'xj-tournament-keys',         title: 'Tournament Keys — ALL',                          description: 'Key attributes for every tournament job',                          iconName: 'trophy',                   kind: 'sp', spName: '[reporting].[TournamentKeyAttributes-ALL]' },
            { id: 'xj-expiring-bulletins',      title: 'Expiring Bulletins (3 mos)',                     description: 'Bulletins expiring within the next three months, all jobs',        iconName: 'hourglass-split',          kind: 'sp', spName: '[utility].[ExpiringBulletins]' },
        ]
    },
    {
        title: 'Accounting',
        iconName: 'cash-stack',
        entries: [
            { id: 'xj-customer-job-revenue',    title: 'Customer Job Revenue',                           description: 'Interactive revenue explorer by customer and job',                 iconName: 'graph-up-arrow',      kind: 'route', route: 'tools/customer-job-revenue' },
            { id: 'xj-new-jobs-last-month',     title: '1) New Jobs Last Month (with txs)',              description: 'Jobs created last month that have transactions',                   iconName: 'plus-square',         kind: 'sp', spName: 'reporting.NewTsicJobsWithTxs' },
            { id: 'xj-adn-eom-iif',             title: '2) Gen: ADN EndOfMonth/IIF',                     description: 'Month-end ADN reconciliation records and IIF generation',          iconName: 'arrow-left-right',    kind: 'route', route: 'accounting/get-reconciliation-records' },
            { id: 'xj-last-months-job-stats',   title: '3) Last Months Job Stats',                       description: 'Per-job registration and revenue stats for last month',            iconName: 'bar-chart-line',      kind: 'route', route: 'accounting/last-months-job-stats' },
            { id: 'xj-grand-totals',            title: "4) Last Month's Grand Totals (Excel)",           description: 'Month-end grand totals across all jobs',                           iconName: 'calculator',          kind: 'sp', spName: 'adn.GetLastMonthsGrandTotals' },
            { id: 'xj-upload-nuvei',            title: 'Upload Nuvei Funding/Batches',                   description: 'Import monthly Nuvei funding and batch exports',                   iconName: 'upload',              kind: 'route', route: 'accounting/upload-nuvei' },
            { id: 'xj-adn-nuvei-reconcile',     title: 'ADN-Nuvei Reconcile (Excel)',                    description: 'Reconcile ADN transactions against Nuvei funding',                 iconName: 'arrow-left-right',    kind: 'sp', spName: 'adn.ReconcileNuvei' },
            { id: 'xj-import-regsaver-payouts', title: 'Import RegSaver Monthly Payouts',                description: 'Import the monthly RegSaver payout file',                          iconName: 'cloud-download',      kind: 'route', route: 'accounting/upload-regsaver' },
            { id: 'xj-job-admin-fees',          title: 'Job Admin Fees Summary',                         description: 'Admin fee summary across all jobs',                                iconName: 'cash-coin',           kind: 'sp', spName: 'reporting.JobAdminFeesAll' },
            { id: 'xj-invoices-last-month',     title: 'Last Months Invoices (pdf)',                     description: 'Prior month customer invoices',                                    iconName: 'file-earmark-pdf',    kind: 'endpoint', endpointPath: 'Get_Invoices_LastMonth' },
            { id: 'xj-invoice-summaries',       title: 'Last Months Invoices SUMMARIES ONLY (pdf)',      description: 'Summaries-only view of prior month customer invoices',             iconName: 'file-earmark-text',   kind: 'endpoint', endpointPath: 'Get_Invoices_LastMonthSummariesOnly' },
            { id: 'xj-manual-arb-sweep',        title: 'Manual ARB Sweep (ALL)',                         description: 'Run the ARB subscription sweep across all jobs',                   iconName: 'arrow-clockwise',     kind: 'route', route: 'accounting/manual-arb-sweep' },
            { id: 'xj-produce-job-invoices',    title: 'Produce Last Month Job Invoices Per Job (rtf)',  description: 'Generate per-job invoice documents for last month',                iconName: 'file-earmark-text',   kind: 'route', route: 'accounting/produce-job-invoices' },
            { id: 'xj-fees-ytd-customer',       title: 'TSIC Fees YTD By Customer',                      description: 'Year-to-date TSIC fees rolled up per customer',                    iconName: 'graph-up',            kind: 'endpoint', endpointPath: 'TSICFeesYTDByCustomer' },
            { id: 'xj-fees-ytd-customer-job',   title: 'TSIC Fees YTD By Customer and Job',              description: 'Year-to-date TSIC fees broken out by customer and job',            iconName: 'graph-up-arrow',      kind: 'endpoint', endpointPath: 'TSICFeesYTDByCustomerAndJob' },
        ]
    }
];
