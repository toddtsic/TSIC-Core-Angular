/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type PrerequisiteCheckResponse = {
    poolsAssigned: boolean;
    unassignedTeamCount: number;
    pairingsCreated: boolean;
    missingPairingTCnts: Array<number>;
    existingPairingRounds: Record<string, number>;
    timeslotsConfigured: boolean;
    agegroupsMissingTimeslots: Array<string>;
    allPassed: boolean;
};

