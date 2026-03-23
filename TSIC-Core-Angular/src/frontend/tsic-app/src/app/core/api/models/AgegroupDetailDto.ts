/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AgegroupDetailDto = {
    agegroupId: string;
    leagueId: string;
    agegroupName?: string | null;
    season?: string | null;
    color?: string | null;
    gender?: string | null;
    dobMin?: string | null;
    dobMax?: string | null;
    gradYearMin?: number | null;
    gradYearMax?: number | null;
    schoolGradeMin?: number | null;
    schoolGradeMax?: number | null;
    teamFee?: number | null;
    teamFeeLabel?: string | null;
    rosterFee?: number | null;
    rosterFeeLabel?: string | null;
    discountFee?: number | null;
    discountFeeStart?: string | null;
    discountFeeEnd?: string | null;
    lateFee?: number | null;
    lateFeeStart?: string | null;
    lateFeeEnd?: string | null;
    maxTeams: number;
    maxTeamsPerClub: number;
    bAllowSelfRostering?: boolean | null;
    bChampionsByDivision?: boolean | null;
    bAllowApiRosterAccess?: boolean | null;
    bHideStandings?: boolean | null;
    playerFeeOverride?: number | null;
    sortAge: number;
};

