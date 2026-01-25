/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegistrationFinancialsDto } from './RegistrationFinancialsDto';
export type ApplyDiscountResponseDto = {
    success?: boolean;
    message?: string | null;
    totalDiscount?: number;
    perPlayer?: Record<string, number>;
    updatedFinancials?: Record<string, RegistrationFinancialsDto>;
};

