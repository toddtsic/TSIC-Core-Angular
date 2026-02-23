/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArbFlagType } from './ArbFlagType';
export type ArbFlaggedRegistrantDto = {
    registrationId: string;
    subscriptionId: string;
    subscriptionStatus: string;
    flagType: ArbFlagType;
    registrantName: string;
    assignment?: string | null;
    familyUsername?: string | null;
    role?: string | null;
    registrantEmail?: string | null;
    momName?: string | null;
    momEmail?: string | null;
    momPhone?: string | null;
    dadName?: string | null;
    dadEmail?: string | null;
    dadPhone?: string | null;
    feeTotal?: number;
    paidTotal?: number;
    currentlyOwes?: number;
    owedTotal?: number;
    nextPaymentDate?: string | null;
    paymentProgress?: string | null;
    jobName: string;
    jobPath: string;
};

