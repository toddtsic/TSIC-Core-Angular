/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AvailableTeamDto = {
    teamId: string;
    teamName: string | null;
    agegroupId?: string;
    agegroupName?: string | null;
    divisionId?: string | null;
    divisionName?: string | null;
    maxRosterSize?: number;
    currentRosterSize?: number;
    rosterIsFull?: boolean;
    teamAllowsSelfRostering?: boolean | null;
    agegroupAllowsSelfRostering?: boolean | null;
    perRegistrantFee?: number | null;
    perRegistrantDeposit?: number | null;
    jobUsesWaitlists?: boolean;
    waitlistTeamId?: string | null;
};

