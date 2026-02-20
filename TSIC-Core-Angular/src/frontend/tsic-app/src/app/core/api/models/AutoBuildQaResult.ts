/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { QaBackToBack } from './QaBackToBack';
import type { QaBracketGame } from './QaBracketGame';
import type { QaDoubleBooking } from './QaDoubleBooking';
import type { QaGamesPerDate } from './QaGamesPerDate';
import type { QaGamesPerFieldPerDay } from './QaGamesPerFieldPerDay';
import type { QaGamesPerTeam } from './QaGamesPerTeam';
import type { QaGamesPerTeamPerDay } from './QaGamesPerTeamPerDay';
import type { QaGameSpread } from './QaGameSpread';
import type { QaInactiveTeamInGame } from './QaInactiveTeamInGame';
import type { QaRankMismatch } from './QaRankMismatch';
import type { QaRepeatedMatchup } from './QaRepeatedMatchup';
import type { QaRrGamesPerDiv } from './QaRrGamesPerDiv';
import type { QaUnscheduledTeam } from './QaUnscheduledTeam';
export type AutoBuildQaResult = {
    totalGames: number;
    unscheduledTeams: Array<QaUnscheduledTeam>;
    fieldDoubleBookings: Array<QaDoubleBooking>;
    teamDoubleBookings: Array<QaDoubleBooking>;
    rankMismatches: Array<QaRankMismatch>;
    backToBackGames: Array<QaBackToBack>;
    repeatedMatchups: Array<QaRepeatedMatchup>;
    inactiveTeamsInGames: Array<QaInactiveTeamInGame>;
    gamesPerDate: Array<QaGamesPerDate>;
    gamesPerTeam: Array<QaGamesPerTeam>;
    gamesPerTeamPerDay: Array<QaGamesPerTeamPerDay>;
    gamesPerFieldPerDay: Array<QaGamesPerFieldPerDay>;
    gameSpreads: Array<QaGameSpread>;
    rrGamesPerDivision: Array<QaRrGamesPerDiv>;
    bracketGames: Array<QaBracketGame>;
};

