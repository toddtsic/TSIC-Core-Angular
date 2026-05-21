/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ClubTeamSummaryDto = {
    teamId: string;
    teamName: string;
    agegroupName: string;
    feeBase: number;
    feeDiscount: number;
    feeLatefee: number;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    feeProcessing: number;
    active: boolean;
    checkFeeReduction?: number;
    paymentScheduled?: boolean;
    nextChargeDate?: string | null;
    paymentFlagged?: boolean;
};

