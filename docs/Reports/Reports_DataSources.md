# Crystal Reports — Report → Data Source Mapping

Each report's data source is a **SQL Server stored procedure** in the `TSICV5` database
(server `.\SS2016`). The controller (`CrystalReportsController.Get`) calls `Load()` + `Refresh()`,
so each report pulls data from the stored procedure embedded in its `.rpt`. Schema qualifiers
below were resolved from `TSICV5` (`INFORMATION_SCHEMA.ROUTINES` / `sys.objects`).

| Report | Data Source (schema.sproc) | Subreport source(s) |
|---|---|---|
| AmericanSelectEvaluation | reporting.AmericanSelectPlayerData | |
| AmericanSelectMainEventRosters | reporting.AmericanSelectPlayerDataMainEvent_Teams | reporting.AmericanSelectPlayerDataMainEvent_TeamRoster |
| AmericanSelectTournyCheckin | reporting.AmericanSelectPlayerData | |
| camp_commuters | reporting.Get_Players_ForExcelExport | |
| camp_datadump | reporting.Get_Players_ForExcelExport | |
| camp_daygroups | reporting.Get_Players_ForExcelExport | |
| camp_daygroups_pdf | reporting.Get_Campers_DayGroups | reporting.Get_Players_ForExcelExport |
| camp_excelexport_daygroups | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_long | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_room_position | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_roomies | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_short | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_summer | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_summer_pdf | reporting.Get_Players_ForExcelExport | |
| camp_excelexport_veryshort | reporting.Get_Players_ForExcelExport | |
| camp_nightgroups | reporting.Get_Players_ForExcelExport | |
| camp_nightgroups_pdf | reporting.Get_Campers_NightGroups | reporting.Get_Players_ForExcelExport |
| camp_roomies | reporting.Get_Players_ForExcelExport | |
| Club_AllJobs_Rosters_NoMedical | reporting.Job_Club_Rosters_AllClubJobs | |
| Club_JobPlayers_Excel | reporting.Get_Players_ForExcelExport_AllCustomers | |
| ClubAllJobPlayerTransactions | reporting.Get_Club_AllJobs_Player_Transactions_ForExcelExport | |
| ClubRep_BalanceDue_ByAgegroupTeamFee | reporting.ClubRep_BalanceDue_ByAgegroupTeamFee | |
| clubrostersNoMedicalII | reporting.Get_Players_ForExcelExport | |
| CovidTournyCheckin | reporting.tournament_checkin_covid | |
| customerInvoiceDataPerMonth | adn.customerInvoiceDataPerMonth | |
| CustomerJobPlayerRollup | reporting.CustomerJobPlayerRollup | |
| CustomerJobRevenueRollups | reporting.CustomerJobRevenueRollups | |
| CustomerJobRevenueTable | reporting.CustomerJobRevenueRollups | |
| CustomerPlayers1 | reporting.Get_Players_ForExcelExport_AllCustomers | |
| discountedplayers | reporting.Get_DiscountedPlayers | |
| FieldUtilizationAcrossLeaguesByDate | reporting.Schedule_ExportExcel | |
| FieldUtilizationAcrossLeaguesByDateTournament | reporting.Schedule_ExportExcel | |
| FieldUtilizationAcrossLeaguesTournament | reporting.Schedule_ExportExcel | |
| FieldUtilizationWithNominations | reporting.Schedule_ExportExcel | |
| invoices2015 | adn.rpt_invoice | |
| invoices2015SummariesOnly | adn.rpt_invoice | |
| invoicesOld | adn.rpt_invoice | |
| ISPCheckinFlat | reporting.ISP_CheckinSheetFlat | |
| ISPGameCheckin | ⚠️ fieldutilizationbyleagueid *(not in TSICV5)* | ⚠️ roster_byteamidISP *(×2, not in TSICV5)* |
| Job_CampCheckin | reporting.Get_Campers | |
| Job_CampCheckinII | reporting.Get_Campers | |
| Job_Club_Rosters | reporting.Job_Club_Rosters | |
| Job_ClubRep_And_Coaches | reporting.Job_ClubReps_And_Coaches | |
| Job_Rosters_NoMedical | reporting.Job_Club_Rosters | |
| JobPlayers_CSCRC_Excel | reporting.Get_Players_ForExcelExport | |
| JobPlayers_E120_Excel | reporting.Get_Players_ForExcelExport | |
| JobPlayers_Liberty | reporting.Get_Players_ForExcelExport | |
| JobPlayers_Showcase_Excel | reporting.Get_Players_ForExcelExport | |
| JobPlayers_STEPS | reporting.Get_Players_STEPS | |
| JobPlayers_STEPS_Excel | reporting.Get_Players_ForExcelExport | |
| JobPlayers_TSICDaily | reporting.Get_Registrations_TSIC_Today | |
| JobPlayers_YJ_Excel | reporting.Get_Players_ForExcelExport | |
| JobPlayerTransactions | reporting.Get_Player_Transactions_ForExcelExport | |
| JobRosters_DayGroupsPackedXPO | reporting.JobRosters_Get_Daygroups | |
| JobRosters_MSYSA | reporting.Job_Club_Rosters_MSYSA | |
| JobRosters_PackedByPosition_DayGroupXPO | reporting.JobRosters_Get_Daygroups | |
| JobRosters_PackedByPosition_XPO | reporting.JobRosters_Get_Teams | reporting.JobRosters_Get_Teamplayers_Withcoach |
| JobRosters_PackedByPositionAG | ⚠️ lftc_rosterreportteams *(not in TSICV5)* | ⚠️ us_rosterreport_getteamplayers *(not in TSICV5)* |
| JobRosters_PackedByPositionAGNoClub | reporting.JobRosters_Get_Teams | reporting.JobRosters_Get_Teamplayers_Withcoach |
| JobRosters_RecruitingReport | reporting.JobRosters_RecruitingReport | |
| JobRosters_RecruitingReport_DumpExcel | reporting.JobRosters_RecruitingReport | |
| JobRosters_RecruitingReport_Public_DumpExcel | reporting.JobRosters_RecruitingReport | |
| JobRosters_RecruitingReport_XPO | reporting.JobRosters_RecruitingReport | |
| JobRosters_TryoutsCheckReport | reporting.JobRosters_TryoutsCheckinReport | |
| JobStaff_Excel | reporting.Get_Staff_ForExcelExport | |
| JobTeams_WithClubRep_AllTransactions_Excel | reporting.Get_TeamsWithClubReps_AllTransactions_ForExcelExport | |
| JobTeams_WithClubRep_Excel | reporting.Get_TeamsWithClubReps_ForExcelExport | |
| JobTeamTransactions | reporting.Get_TeamsWithClubReps_AllTransactions_ForExcelExport | |
| League_ClubReps | reporting.League_ClubReps | |
| League_Coaches | reporting.League_Coaches | |
| League_Standings | reporting.Standings_Get | |
| League_StandingsExcel | reporting.Standings_League_Get | |
| League_Teams | reporting.League_Teams | |
| LeagueForfeitReport | reporting.AAYSA_Forfeits_Report | |
| LeagueRefReport | reporting.AAYSA_RefReconcilliation | |
| Mobile_JobUsers | mobile.UsageByJobAndRegistrant | |
| outdooredrosters | reporting.Get_OERosters | |
| outdooredrostersspringonly | reporting.Get_OERosters_SpringOnly | |
| PlayerStats_E120 | reporting.PlayerStats_E120 | |
| PlayerStats_ParisiExportExcel | reporting.PlayerStats_ParisiResults | |
| Rosters_WithClubRep_A | reporting.Get_Rosters_WithClubRep | |
| Schedule_ByAgegroup | reporting.Schedule_Get_AgegroupScorecard | reporting.Schedule_Get_DivTeamsAndStandings |
| Schedule_Export | reporting.Schedule_Export | |
| Schedule_Export_Public | reporting.Schedule_Export_Public | |
| Schedule_ExportExcel | reporting.Schedule_ExportExcel | |
| Schedule_ExportExcel_Unscored | reporting.Schedule_ExportExcel | |
| schedule_gamecards | reporting.Schedule_ExportExcel | |
| ScheduleByAgDiv | reporting.Schedule_ExportExcel | |
| ScheduleByClubAgT | reporting.ScheduleTeamViewData | |
| ScheduleByClubAgTPerPage | reporting.ScheduleTeamViewData | |
| ScheduleByDay | reporting.Schedule_ExportExcel | |
| ScheduleMaster | reporting.Schedule_ExportExcel | |
| Score_Input | reporting.Schedule_RecordScores | |
| StoreLabels3 | reporting.StoreLabels | |
| StorePerPlayerPickup | reporting.StorePickupConfirmation | |
| StorePerPlayerPickup-OLD | reporting.StorePickupConfirmation | |
| StorePerPlayerPivot | reporting.StorePickupConfirmation | |
| StorePickupSignoff | reporting.StorePickupConfirmation | |
| teamfielddistribution | reporting.TeamsFieldDistribution | |
| TestMultipleTableResults | reporting.TestMultipleTableResults | |
| TournamentPlayerDumpForRecruiters | reporting.JobRosters_ExportTournament † | |
| TournamentPlayers_RosterRequestAndRegistrants_DataDump | reporting.JobRosters_ExportTournament_WithRosterRequests | |
| tournamentrecruitingreport | reporting.JobRosters_ExportTournament † | |
| TournamentRecruitingReport_DataDump | reporting.JobRosters_ExportTournament † | |
| tournamentrecruitingreportASL | reporting.JobRosters_ExportTournament † | |
| tournamentrecruitingreportUSL | reporting.JobRosters_ExportTournament † | |
| tournamentrecruitingreportUSLOld | reporting.JobRosters_ExportTournament † | |
| TournamentRosterPacked | reporting.tourneyteams_for_masterdetail | reporting.JobRosters_ExportTournament_ByTeam |
| TournamentRosterPacked_PositionSchool | reporting.tourneyteams_for_masterdetail | reporting.JobRosters_ExportTournament_ByTeam |
| TournamentRosterPacked_PositionSchool_Public | reporting.tourneyteams_for_masterdetail | reporting.JobRosters_ExportTournament_ByTeam |
| tournamentumml1 - Copy | reporting.JobRosters_ExportTournament † | |
| tournamentumml1 | reporting.JobRosters_ExportTournament † | |
| tournamentumml2 | reporting.JobRosters_ExportTournament † | |
| tournycheckin | reporting.tournament_checkin | |
| tsicTSICFeesYTD | adn.tsicFeesYTDAndLastYear | |
| tsicTSICFeesYTDByCustomer | adn.tsicFeesYTDAndLastYear | |
| UniformData | reporting.UniformData | |

## Notes

**Schema summary**
- Most reports → `reporting`
- Invoices / fees → `adn` (`adn.rpt_invoice`, `adn.customerInvoiceDataPerMonth`, `adn.tsicFeesYTDAndLastYear`)
- `Mobile_JobUsers` → `mobile.UsageByJobAndRegistrant`

**Flags**
- **† `JobRosters_ExportTournament`** exists in **two** schemas — `reporting` and `reporting_migrate`.
  Crystal stored only the unqualified proc name, so the schema that actually runs depends on the
  login's default schema at runtime. `reporting` is listed as the live one (`reporting_migrate` is a staging copy).
- **⚠️ `ISPGameCheckin`** and **`JobRosters_PackedByPositionAG`** reference stored procedures not present
  in `TSICV5` (`fieldutilizationbyleagueid`, `roster_byteamidISP`, `lftc_rosterreportteams`,
  `us_rosterreport_getteamplayers`). They will fail unless their embedded `.rpt` connection points to a
  different database.
