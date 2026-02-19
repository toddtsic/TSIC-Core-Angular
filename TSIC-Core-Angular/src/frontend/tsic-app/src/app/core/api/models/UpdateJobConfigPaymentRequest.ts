/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type UpdateJobConfigPaymentRequest = {
    paymentMethodsAllowedCode: number;
    bAddProcessingFees: boolean;
    processingFeePercent: number;
    bApplyProcessingFeesToTeamDeposit: boolean | null;
    perPlayerCharge: number;
    perTeamCharge: number;
    perMonthCharge: number;
    payTo: string | null;
    mailTo: string | null;
    mailinPaymentWarning: string | null;
    balancedueaspercent: string | null;
    bTeamsFullPaymentRequired: boolean | null;
    bAllowRefundsInPriorMonths: boolean | null;
    bAllowCreditAll: boolean | null;
    adnArb?: boolean | null;
    adnArbBillingOccurrences?: number;
    adnArbIntervalLength?: number;
    adnArbStartDate?: string | null;
    adnArbMinimumTotalCharge?: number;
};

