/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type CreateAgegroupRequest = {
    leagueId: string;
    agegroupName: string;
    color?: string | null;
    gender?: string | null;
    dobMin?: string | null;
    dobMax?: string | null;
    gradYearMin?: number | null;
    gradYearMax?: number | null;
    schoolGradeMin?: number | null;
    schoolGradeMax?: number | null;
    maxTeams?: number;
    maxTeamsPerClub?: number;
    bAllowSelfRostering?: boolean | null;
    bChampionsByDivision?: boolean | null;
    bAllowApiRosterAccess?: boolean | null;
    bHideStandings?: boolean | null;
    sortAge?: number;
};

