/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type JobConfigPaymentDto = {
    paymentMethodsAllowedCode: number;
    bAddProcessingFees: boolean;
    processingFeePercent: number | null;
    minProcessingFeePercent: number;
    maxProcessingFeePercent: number;
    bEnableEcheck: boolean;
    ecprocessingFeePercent: number | null;
    minEcprocessingFeePercent: number;
    maxEcprocessingFeePercent: number;
    bApplyProcessingFeesToTeamDeposit: boolean | null;
    perPlayerCharge: number | null;
    perTeamCharge: number | null;
    perMonthCharge: number | null;
    payTo: string | null;
    mailTo: string | null;
    mailinPaymentWarning: string | null;
    balancedueaspercent: string | null;
    bTeamsFullPaymentRequired: boolean | null;
    bPlayersFullPaymentRequired: boolean;
    bAllowRefundsInPriorMonths: boolean | null;
    bAllowCreditAll: boolean | null;
    adnArb?: boolean | null;
    adnArbBillingOccurrences?: number | null;
    adnArbIntervalLength?: number | null;
    adnArbStartDate?: string | null;
    adnArbMinimumTotalCharge?: number | null;
    adnArbTrial?: boolean | null;
    adnStartDateAfterTrial?: string | null;
    adminCharges?: any[] | null;
};

