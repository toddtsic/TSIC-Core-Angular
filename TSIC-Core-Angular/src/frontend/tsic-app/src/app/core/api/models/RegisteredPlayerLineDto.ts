/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RegisteredPlayerLineDto = {
    registrationId: string;
    playerName: string;
    teamName: string | null;
    ageGroupName: string | null;
    isWaitlisted?: boolean;
    ageGroupDisplayName?: string | null;
    clubName: string | null;
    active: boolean;
    registrationTs: string;
    feeBase: number;
    feeProcessing: number;
    feeProcessingDue: number;
    feeDiscount: number;
    feeLatefee: number;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    feeAdj: number;
    tenderPaid: number;
    deposit: number;
    balanceDue: number;
    fullPaymentRequired: boolean;
    depositDue: number;
    additionalDue: number;
    ccOwedTotal: number;
    ckOwedTotal: number;
    ekOwedTotal: number;
};

