/**
 * Type 1 (Crystal Reports) catalogue — hard-coded.
 *
 * Each entry maps to an existing ReportingController [HttpGet("X")] route;
 * `endpointPath` is the exact route name (case-sensitive for documentation
 * even though ASP.NET resolves case-insensitive). The component calls
 * ReportingService.downloadReport(endpointPath, queryParams?) to run.
 *
 * Auth is enforced server-side per-endpoint (AllowAnonymous / AdminOnly /
 * StoreAdmin / Superuser). Clicking a report the user can't access returns
 * 403 — the component surfaces that, doesn't pre-gate.
 *
 * Visibility rules intentionally omitted for now — surfaces all Type 1
 * reports to admins on any job. Per-JobType gating can be added later
 * once the legacy -> new JobTypeName mapping is settled.
 *
 * Sort order = descending by legacy usage (2025-2027 JobCount). Most-used
 * reports float to the top.
 */

import type { VisibilityRules } from './visibility-rules';

export interface Type1ReportEntry {
    id: string;
    title: string;
    description?: string;
    iconName?: string;
    endpointPath: string;
    visibilityRules?: VisibilityRules;
    sortOrder: number;
}

export const TYPE1_REPORT_CATALOG: readonly Type1ReportEntry[] = [
    { id: 't1-player-transactions',             title: 'Player Transactions (Excel)',           description: 'Per-player transaction history for the job',               iconName: 'receipt',              endpointPath: 'Get_JobPlayer_Transactions',                       sortOrder: 10 },
    { id: 't1-net-users',                       title: 'Net Users (pdf)',                       description: 'Network-level user count across the platform',             iconName: 'people',               endpointPath: 'Get_NetUsers',                                     sortOrder: 20 },
    { id: 't1-player-data-steps-excel',         title: 'Player Data (STEPS Excel)',             description: 'Player roster export in STEPS column layout',              iconName: 'person-lines-fill',    endpointPath: 'Get_JobPlayers_STEPS_Excel',                       sortOrder: 30 },
    { id: 't1-staff-data',                      title: 'Staff Data (Excel)',                    description: 'Staff roster export',                                      iconName: 'people-fill',          endpointPath: 'JobStaff_Excel',                                   sortOrder: 40 },
    { id: 't1-field-utilization-tourn',         title: 'Field Utilization - Tournament (pdf)',  description: 'Tournament field-hour utilization summary',                iconName: 'geo-alt',              endpointPath: 'FieldUtilizationAcrossLeaguesTournament',          sortOrder: 50 },
    { id: 't1-field-utilization-tourn-by-date', title: 'Field Utilization by Date (pdf)',       description: 'Tournament field utilization sliced by date',              iconName: 'calendar-date',        endpointPath: 'FieldUtilizationAcrossLeaguesByDateTournament',    sortOrder: 60 },
    { id: 't1-schedule-by-agegroup',            title: 'Game Boards by Agegroup (pdf)',         description: 'Printable game boards grouped by agegroup',                iconName: 'grid-3x3',             endpointPath: 'Schedule_ByAgegroup',                              sortOrder: 70 },
    { id: 't1-tourny-checkin',                  title: 'Tournament Check-In (pdf)',             description: 'Tournament team check-in sheets',                          iconName: 'clipboard-check',      endpointPath: 'TournyCheckin',                                    sortOrder: 80 },
    { id: 't1-recruiting-report',               title: 'Recruiting Report (pdf)',               description: 'Tournament recruiting summary for college coaches',        iconName: 'journal-bookmark',     endpointPath: 'TournamentRecruitingReport',                       sortOrder: 90 },
    { id: 't1-recruiting-dump',                 title: 'Recruiting Data Dump (Excel)',          description: 'Full data export of recruiting report',                    iconName: 'file-earmark-spreadsheet', endpointPath: 'TournamentRecruitingReport_DataDump',          sortOrder: 100 },
    { id: 't1-rosters-packed-noclub',           title: 'Rosters Packed - No Club Players (pdf)', description: 'Packed rosters by position, excluding club players',      iconName: 'layers',               endpointPath: 'Get_JobRosters_PackedByPositionAGNoClubPlayers',   sortOrder: 110 },
    { id: 't1-rosters-recruiting-excel',        title: 'Recruiting Rosters Data Dump (Excel)',  description: 'Recruiting-report rosters in dumpable Excel',              iconName: 'file-earmark-excel',   endpointPath: 'Get_JobRosters_RecruitingReport_DumpExcel',        sortOrder: 120 },
    { id: 't1-rosters-recruiting-public',       title: 'Recruiting Rosters (Public, Excel)',    description: 'Public-facing recruiting roster dump',                     iconName: 'share',                endpointPath: 'Get_JobRosters_RecruitingReport_Public_DumpExcel', sortOrder: 130 },
    { id: 't1-rosters-recruiting-pdf',          title: 'Recruiting Rosters Report (pdf)',       description: 'Formatted recruiting roster PDF',                          iconName: 'file-earmark-pdf',     endpointPath: 'Get_JobRosters_RecruitingReport',                  sortOrder: 140 },
    { id: 't1-player-details-yj',               title: 'Player Details (YJ Excel)',             description: 'Full player detail export (YJ format)',                    iconName: 'person-vcard',         endpointPath: 'JobPlayers_YJ_Excel',                              sortOrder: 150 },
    { id: 't1-tournament-rosters-school',       title: 'Tournament Rosters Packed - by School (pdf)', description: 'Packed tournament rosters grouped by school',        iconName: 'mortarboard',          endpointPath: 'TournamentRosterPacked_PositionSchool',            sortOrder: 160 },
    { id: 't1-american-select-checkin',         title: 'American Select Check-In (pdf)',        description: 'Tournament check-in for American Select',                  iconName: 'clipboard2-check',     endpointPath: 'AmericanSelectTournyCheckin',                      sortOrder: 170 },
    { id: 't1-american-select-eval',            title: 'American Select Evaluation (pdf)',      description: 'Player evaluation sheet for American Select',              iconName: 'clipboard-data',       endpointPath: 'AmericanSelectEvaluation',                         sortOrder: 180 },
    { id: 't1-american-select-main-event',      title: 'American Select Main Event Rosters (pdf)', description: 'Main event roster sheets for American Select',          iconName: 'trophy',               endpointPath: 'AmericanSelectMainEventRosters',                   sortOrder: 190 },
    { id: 't1-club-rosters-nomedical',          title: 'Club Rosters - No Medical (pdf)',       description: 'Club roster pack without medical columns',                 iconName: 'shield',               endpointPath: 'Job_Rosters_NoMedical',                            sortOrder: 200 },
    { id: 't1-discounted-players',              title: 'Discounted Players (Excel)',            description: 'Players holding a discount code',                          iconName: 'tags',                 endpointPath: 'Get_DiscountedPlayers',                            sortOrder: 210 },
    { id: 't1-player-data-all-jobs',            title: 'Player Data - All Customer Jobs (Excel)', description: 'Cross-job player roster for a customer',                 iconName: 'building',             endpointPath: 'Get_CustomerPlayers1',                             sortOrder: 220 },
    { id: 't1-tournament-rosters-packed',       title: 'Tournament Rosters Packed (pdf)',       description: 'Standard packed tournament roster sheets',                 iconName: 'list-ul',              endpointPath: 'TournamentRosterPacked',                           sortOrder: 230 },
    { id: 't1-playerstats-e120',                title: 'Player Stats E120 Entry Form (pdf)',    description: 'E120 stats entry form',                                    iconName: 'pencil-square',        endpointPath: 'PlayerStats_E120',                                 sortOrder: 240 },
    { id: 't1-player-data-e120-excel',          title: 'Player Data (E120 Excel)',              description: 'Player roster in E120 column layout',                      iconName: 'person-lines-fill',    endpointPath: 'Get_JobPlayers_E120_Excel',                        sortOrder: 250 },
    { id: 't1-active-players-steps-pdf',        title: 'Active Players Report (STEPS pdf)',     description: 'Active-players summary in STEPS layout',                   iconName: 'person-check',         endpointPath: 'Get_JobPlayers_STEPS',                             sortOrder: 260 },
    { id: 't1-club-rosters',                    title: 'Club Rosters (pdf)',                    description: 'Standard club roster pack',                                iconName: 'people',               endpointPath: 'Job_Club_Rosters',                                 sortOrder: 270 },
    { id: 't1-camp-checkin',                    title: 'Camp Check-In (pdf)',                   description: 'Camp arrival check-in sheets',                             iconName: 'clipboard2',           endpointPath: 'Job_CampCheckin',                                  sortOrder: 280 },
    { id: 't1-camp-datadump',                   title: 'Camp Data Dump (Excel)',                description: 'Raw camp export — all columns',                           iconName: 'database',             endpointPath: 'camp_datadump',                                    sortOrder: 290 },
    { id: 't1-camp-export-long',                title: 'Camp Export - Long (Excel)',            description: 'Long-form camp export',                                    iconName: 'file-earmark-excel',   endpointPath: 'camp_excelexport_long',                            sortOrder: 300 },
    { id: 't1-camp-export-short',               title: 'Camp Export - Short (Excel)',           description: 'Short-form camp export',                                   iconName: 'file-earmark-excel',   endpointPath: 'camp_excelexport_short',                           sortOrder: 310 },
    { id: 't1-camp-export-veryshort',           title: 'Camp Export - Very Short (Excel)',      description: 'Minimal camper export',                                    iconName: 'file-earmark-excel',   endpointPath: 'camp_excelexport_veryshort',                       sortOrder: 320 },
    { id: 't1-club-rosters-coaches-ii',         title: 'Club Rosters for Coaches (pdf)',        description: 'Coach-facing club roster pack',                            iconName: 'clipboard-heart',      endpointPath: 'clubrostersNoMedicalII',                           sortOrder: 330 },
    { id: 't1-camp-commuters',                  title: 'Camp Commuters (pdf)',                  description: 'Day commuter roster',                                      iconName: 'house',                endpointPath: 'camp_commuters',                                   sortOrder: 340 },
    { id: 't1-camp-daygroups-excel',            title: 'Camp Day/Night Groups (Excel)',         description: 'Camp daygroup + nightgroup rosters',                       iconName: 'file-earmark-excel',   endpointPath: 'camp_excelexport_daygroups',                       sortOrder: 350 },
    { id: 't1-camp-daygroups-pdf',              title: 'Camp Daygroups (pdf)',                  description: 'Daygroup rosters',                                         iconName: 'sun',                  endpointPath: 'camp_daygroups',                                   sortOrder: 360 },
    { id: 't1-camp-daygroups-stacked',          title: 'Camp Daygroups - Stacked (pdf)',        description: 'Stacked-layout daygroup sheets',                           iconName: 'stack',                endpointPath: 'camp_daygroups_pdf',                               sortOrder: 370 },
    { id: 't1-camp-nightgroups-pdf',            title: 'Camp Nightgroups (pdf)',                description: 'Nightgroup rosters',                                       iconName: 'moon',                 endpointPath: 'camp_nightgroups',                                 sortOrder: 380 },
    { id: 't1-camp-nightgroups-stacked',        title: 'Camp Nightgroups - Stacked (pdf)',      description: 'Stacked-layout nightgroup sheets',                         iconName: 'stack',                endpointPath: 'camp_nightgroups_pdf',                             sortOrder: 390 },
    { id: 't1-camp-roomies-excel',              title: 'Camp Roommates (Excel)',                description: 'Roommate list export',                                     iconName: 'file-earmark-excel',   endpointPath: 'camp_excelexport_roomies',                         sortOrder: 400 },
    { id: 't1-camp-roomies-pdf',                title: 'Camp Roommates (pdf)',                  description: 'Printable roommate list',                                  iconName: 'house-heart',          endpointPath: 'camp_roomies',                                     sortOrder: 410 },
    { id: 't1-camp-roomies-position',           title: 'Camp Roommates with Position (Excel)',  description: 'Roommate list including playing position',                 iconName: 'diagram-3',            endpointPath: 'camp_excelexport_room_position',                   sortOrder: 420 },
    { id: 't1-playerstats-parisi-excel',        title: 'Player Stats Export - Parisi (Excel)',  description: 'Parisi lab stats export',                                  iconName: 'speedometer2',         endpointPath: 'PlayerStats_ParisiExportExcel',                    sortOrder: 430 },
    { id: 't1-rosters-packed-xpo',              title: 'Rosters Packed XPO (pdf)',              description: 'XPO-format packed rosters',                                iconName: 'layers',               endpointPath: 'Get_JobRosters_PackedByPosition_XPO',              sortOrder: 440 },
    { id: 't1-rosters-recruiting-xpo',          title: 'Recruiting Rosters XPO (pdf)',          description: 'XPO-format recruiting rosters',                            iconName: 'journal-bookmark',     endpointPath: 'Get_JobRosters_RecruitingReport_XPO',              sortOrder: 450 },
    { id: 't1-team-field-distribution',         title: 'Team Field Distribution',               description: 'Distribution of teams across fields',                      iconName: 'diagram-2',            endpointPath: 'Get_TeamFieldDistribution',                        sortOrder: 460 },
    { id: 't1-mobile-users',                    title: 'Mobile Users (Excel)',                  description: 'Mobile app user list for the job',                         iconName: 'phone',                endpointPath: 'Mobile_JobUsers',                                  sortOrder: 470 },
    { id: 't1-tryouts-check',                   title: 'Tryouts Check-In Report (pdf)',         description: 'Player check-in sheet for tryouts',                        iconName: 'clipboard-check',      endpointPath: 'JobRosters_TryoutsCheckReport',                    sortOrder: 480 },
    { id: 't1-daygroups-packed-xpo',            title: 'Daygroups Packed XPO (pdf)',            description: 'XPO-format daygroup packed sheets',                        iconName: 'stack',                endpointPath: 'JobRosters_DayGroupsPackedXPO',                    sortOrder: 490 },
    { id: 't1-isp-checkin-flat',                title: 'ISP Check-In (Flat)',                   description: 'Flat ISP check-in layout',                                 iconName: 'clipboard',            endpointPath: 'ISP_CheckinFlat',                                  sortOrder: 500 },
    { id: 't1-player-data-liberty-excel',       title: 'Player Data - Liberty (Excel)',         description: 'Player roster in Liberty column layout',                   iconName: 'person-lines-fill',    endpointPath: 'Get_JobPlayers_Liberty_Excel',                     sortOrder: 510 },
    { id: 't1-score-input',                     title: 'Score Entry Sheets',                    description: 'Blank score-entry sheets for officials',                   iconName: 'pencil-square',        endpointPath: 'Score_Input',                                      sortOrder: 520 },
];
