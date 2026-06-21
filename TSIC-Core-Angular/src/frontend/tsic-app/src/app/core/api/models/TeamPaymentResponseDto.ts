/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TeamPaymentResultDto } from './TeamPaymentResultDto';
export type TeamPaymentResponseDto = {
    success: boolean;
    transactionId?: string | null;
    error?: string | null;
    message?: string | null;
    teams?: Array<TeamPaymentResultDto>;
};

