/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PoolClubRepImpactDto } from './PoolClubRepImpactDto';
import type { PoolTransferPreviewDto } from './PoolTransferPreviewDto';
export type PoolTransferPreviewResponse = {
    teams: Array<PoolTransferPreviewDto>;
    clubRepImpacts: Array<PoolClubRepImpactDto>;
    hasScheduledTeams: boolean;
    requiresSymmetricalSwap: boolean;
};

