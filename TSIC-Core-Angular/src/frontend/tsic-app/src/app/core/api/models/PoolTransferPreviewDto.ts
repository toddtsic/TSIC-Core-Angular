/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type PoolTransferPreviewDto = {
    teamId: string;
    teamName: string;
    direction: string;
    agegroupChanges: boolean;
    currentFeeBase: number;
    currentFeeTotal: number;
    newFeeBase: number;
    newFeeTotal: number;
    feeDelta: number;
    isScheduled: boolean;
    requiresSymmetricalSwap: boolean;
    warning?: string | null;
};

