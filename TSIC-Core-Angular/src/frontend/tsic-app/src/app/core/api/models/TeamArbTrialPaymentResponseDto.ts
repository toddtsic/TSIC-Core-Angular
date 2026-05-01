/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TeamArbTrialResultDto } from './TeamArbTrialResultDto';
export type TeamArbTrialPaymentResponseDto = {
    success: boolean;
    mode?: string | null;
    error?: string | null;
    message?: string | null;
    teams: Array<TeamArbTrialResultDto>;
    notAttempted: Array<string>;
};

