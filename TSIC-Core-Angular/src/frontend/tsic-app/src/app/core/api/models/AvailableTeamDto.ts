/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AvailableTeamDto = {
    teamId: string;
    teamName: string;
    agegroupId: string;
    agegroupName?: string | null;
    divisionId?: string | null;
    divisionName?: string | null;
    maxRosterSize: number;
    currentRosterSize: number;
    rosterIsFull: boolean;
    teamAllowsSelfRostering?: boolean | null;
    agegroupAllowsSelfRostering?: boolean | null;
    fee?: number | null;
    deposit?: number | null;
    effectiveFee?: number | null;
    jobUsesWaitlists: boolean;
    waitlistTeamId?: string | null;
    startDate?: string | null;
    endDate?: string | null;
    perRegistrantFee?: number | null;
};

