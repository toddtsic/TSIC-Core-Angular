/**
 * Type 1 (Crystal Reports) catalogue — hard-coded.
 *
 * Each entry maps to an existing ReportingController [HttpGet("X")] route;
 * `endpointPath` is the exact route name. The component calls
 * ReportingService.downloadReport(endpointPath, queryParams?) to run.
 *
 * Auth is enforced server-side per-endpoint (AllowAnonymous / AdminOnly /
 * StoreAdmin / Superuser). Clicking a report the user can't access returns
 * 403 — the component surfaces that, doesn't pre-gate.
 *
 * visibilityRules.jobTypes is derived from legacy 2025-2027 menu usage
 * (Jobs.JobMenus + JobMenu_Items, Controller='Reporting') — each entry is
 * only offered on job types where it was historically used. Reports that
 * spanned all six JobTypes (e.g. Discounted Players) are left unfiltered.
 *
 * `category` drives reports-library grouping; values must come from
 * ReportCategory union in report-categories.ts.
 *
 * Sort order = descending by legacy usage (2025-2027 JobCount).
 */

import type { VisibilityRules } from './visibility-rules';
import type { ReportCategory } from './report-categories';

export interface Type1ReportEntry {
    id: string;
    title: string;
    description?: string;
    iconName?: string;
    endpointPath: string;
    visibilityRules?: VisibilityRules;
    category: ReportCategory;
    sortOrder: number;
}

// --- JobType allowlist presets (keeps rows readable and dedupes typos) ---
const JT = {
    Camp:      'Camp Registration',
    ClubSport: 'Club Sport Registration',
    League:    'League Scheduling',
    Sales:     'Sales Venue',
    Showcase:  'Showcase Registration',
    Tournament:'Tournament Scheduling',
} as const;

const PRE_LEAGUE_TOURNAMENT: VisibilityRules = { jobTypes: [JT.League, JT.Tournament] };
const PRE_CLUB_SHOWCASE:     VisibilityRules = { jobTypes: [JT.ClubSport, JT.Showcase] };
const PRE_CAMP_TRIO:         VisibilityRules = { jobTypes: [JT.Sales, JT.Camp, JT.Showcase] };
const PRE_CAMP_PAIR:         VisibilityRules = { jobTypes: [JT.Sales, JT.Camp] };
const PRE_SHOWCASE_ONLY:     VisibilityRules = { jobTypes: [JT.Showcase] };
const PRE_CLUB_ONLY:         VisibilityRules = { jobTypes: [JT.ClubSport] };

export const TYPE1_REPORT_CATALOG: readonly Type1ReportEntry[] = [
    { id: 't1-schedule-by-agegroup',            title: 'Game Boards by Agegroup (pdf)',              description: 'Printable game boards grouped by agegroup',                 iconName: 'grid-3x3',                  endpointPath: 'Schedule_ByAgegroup',                              visibilityRules: { jobTypes: [JT.Showcase, JT.League, JT.Tournament] }, category: 'Schedules',     sortOrder: 70 },
    { id: 't1-tournament-rosters-school',       title: 'Tournament Rosters Packed - by School (pdf)', description: 'Packed tournament rosters grouped by school',              iconName: 'mortarboard',               endpointPath: 'TournamentRosterPacked_PositionSchool',            visibilityRules: PRE_LEAGUE_TOURNAMENT, category: 'Rosters',       sortOrder: 160 },
    { id: 't1-american-select-eval',            title: 'American Select Evaluation (pdf)',           description: 'Player evaluation sheet for American Select',               iconName: 'clipboard-data',            endpointPath: 'AmericanSelectEvaluation',                         visibilityRules: PRE_SHOWCASE_ONLY, category: 'Rosters',           sortOrder: 180 },
    { id: 't1-american-select-main-event',      title: 'American Select Main Event Rosters (pdf)',   description: 'Main event roster sheets for American Select',              iconName: 'trophy',                    endpointPath: 'AmericanSelectMainEventRosters',                   visibilityRules: PRE_SHOWCASE_ONLY, category: 'Rosters',           sortOrder: 190 },
    { id: 't1-playerstats-e120',                title: 'Player Stats E120 Entry Form (pdf)',         description: 'E120 stats entry form',                                     iconName: 'pencil-square',             endpointPath: 'PlayerStats_E120',                                 visibilityRules: PRE_CLUB_SHOWCASE, category: 'Rosters',           sortOrder: 240 },
    { id: 't1-camp-commuters',                  title: 'Camp Commuters (pdf)',                       description: 'Day commuter roster',                                       iconName: 'house',                     endpointPath: 'camp_commuters',                                   visibilityRules: PRE_CAMP_PAIR, category: 'Camp',                  sortOrder: 340 },

    // ── SuperUser cross-customer reports (TSIC home / monthly close) ──────
    // Run from TSIC home job; intentionally not job-type-gated since they cover ALL jobs.
    // Daily Reg Counts is BE-anonymous but legacy menu placed it under SuperUser — gate
    // here to match. The Crystal accounting items are AdminOnly server-side, but legacy
    // showed them only to SuperUser.
    { id: 't1-su-daily-reg-counts',             title: 'Daily Registration Counts (PDF)',            description: 'TSIC daily registration count report',                      iconName: 'calendar-check',            endpointPath: 'Get_JobPlayers_TSICDAILY',                         visibilityRules: { requiresRoles: ['Superuser'] }, category: 'Administration', sortOrder: 1010 },
    { id: 't1-su-tsic-fees-ytd-customer',       title: 'TSIC Fees YTD by Customer',                  description: 'Year-to-date TSIC fees rolled up per customer',             iconName: 'graph-up',                  endpointPath: 'TSICFeesYTDByCustomer',                            visibilityRules: { requiresRoles: ['Superuser'] }, category: 'Financials',     sortOrder: 1020 },
    { id: 't1-su-tsic-fees-ytd-customer-job',   title: 'TSIC Fees YTD by Customer + Job',            description: 'Year-to-date TSIC fees broken out by customer and job',     iconName: 'graph-up-arrow',            endpointPath: 'TSICFeesYTDByCustomerAndJob',                      visibilityRules: { requiresRoles: ['Superuser'] }, category: 'Financials',     sortOrder: 1030 },
    { id: 't1-su-last-month-invoices',          title: 'Last Month Invoices (PDF)',                  description: 'Prior month customer invoices',                             iconName: 'file-earmark-pdf',          endpointPath: 'Get_Invoices_LastMonth',                           visibilityRules: { requiresRoles: ['Superuser'] }, category: 'Financials',     sortOrder: 1040 },
    { id: 't1-su-last-month-invoice-summaries', title: 'Last Month Invoice Summaries (PDF)',         description: 'Summaries-only view of prior month customer invoices',      iconName: 'file-earmark-text',         endpointPath: 'Get_Invoices_LastMonthSummariesOnly',              visibilityRules: { requiresRoles: ['Superuser'] }, category: 'Financials',     sortOrder: 1050 },
];
