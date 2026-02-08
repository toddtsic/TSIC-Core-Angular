/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type UpdateAgegroupRequest = {
    agegroupName?: string | null;
    season?: string | null;
    color?: string | null;
    gender?: string | null;
    dobMin?: string | null;
    dobMax?: string | null;
    gradYearMin?: number;
    gradYearMax?: number;
    schoolGradeMin?: number;
    schoolGradeMax?: number;
    teamFee?: number;
    teamFeeLabel?: string | null;
    rosterFee?: number;
    rosterFeeLabel?: string | null;
    discountFee?: number;
    discountFeeStart?: string | null;
    discountFeeEnd?: string | null;
    lateFee?: number;
    lateFeeStart?: string | null;
    lateFeeEnd?: string | null;
    maxTeams?: number;
    maxTeamsPerClub?: number;
    bAllowSelfRostering?: boolean | null;
    bChampionsByDivision?: boolean | null;
    bAllowApiRosterAccess?: boolean | null;
    bHideStandings?: boolean | null;
    playerFeeOverride?: number;
    sortAge?: number;
};

