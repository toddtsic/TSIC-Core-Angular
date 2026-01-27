/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TeamDiscountResult } from './TeamDiscountResult';
export type ApplyTeamDiscountResponseDto = {
    success: boolean;
    message: string | null;
    totalTeamsProcessed: number;
    successCount: number;
    failureCount: number;
    results: Array<TeamDiscountResult>;
};

