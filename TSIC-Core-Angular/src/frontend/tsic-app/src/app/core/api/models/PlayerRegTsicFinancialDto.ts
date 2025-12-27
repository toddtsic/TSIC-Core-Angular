/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlayerRegFinancialLineDto } from './PlayerRegFinancialLineDto';
export type PlayerRegTsicFinancialDto = {
    wasImmediateCharge: boolean;
    wasArb: boolean;
    amountCharged: number;
    currency: string;
    transactionId: string | null;
    paymentMethodMasked: string | null;
    nextArbBillDate: string | null;
    totalOriginal: number;
    totalDiscounts: number;
    totalNet: number;
    lines: Array<PlayerRegFinancialLineDto>;
};

