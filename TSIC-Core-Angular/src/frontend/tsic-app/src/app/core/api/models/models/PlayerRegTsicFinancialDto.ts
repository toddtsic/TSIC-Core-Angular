/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlayerRegFinancialLineDto } from './PlayerRegFinancialLineDto';
export type PlayerRegTsicFinancialDto = {
    wasImmediateCharge: boolean;
    wasArb: boolean;
    amountCharged: number | string;
    currency: string;
    transactionId: string | null;
    paymentMethodMasked: string | null;
    nextArbBillDate: string | null;
    totalOriginal: number | string;
    totalDiscounts: number | string;
    totalNet: number | string;
    lines: Array<PlayerRegFinancialLineDto>;
};

