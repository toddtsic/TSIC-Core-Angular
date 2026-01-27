/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlayerDiscountResult } from './PlayerDiscountResult';
import type { RegistrationFinancialsDto } from './RegistrationFinancialsDto';
export type ApplyDiscountResponseDto = {
    success: boolean;
    message: string | null;
    totalDiscount: number;
    totalPlayersProcessed: number;
    successCount: number;
    failureCount: number;
    results: Array<PlayerDiscountResult>;
    updatedFinancials?: Record<string, RegistrationFinancialsDto>;
};

