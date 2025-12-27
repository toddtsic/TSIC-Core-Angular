/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AvailableTeamDto = {
    teamId: string;
    teamName: string;
    agegroupId?: string;
    agegroupName?: string | null;
    divisionId?: string | null;
    divisionName?: string | null;
    maxRosterSize?: number | string;
    currentRosterSize?: number | string;
    rosterIsFull?: boolean;
    teamAllowsSelfRostering?: boolean | null;
    agegroupAllowsSelfRostering?: boolean | null;
    perRegistrantFee?: number | string | null;
    perRegistrantDeposit?: number | string | null;
    jobUsesWaitlists?: boolean;
    waitlistTeamId?: string | null;
};

