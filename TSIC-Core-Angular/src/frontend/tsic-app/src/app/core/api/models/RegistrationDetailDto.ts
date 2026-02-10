/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
export type RegistrationDetailDto = {
    registrationId: string;
    registrationAi: number;
    firstName: string;
    lastName: string;
    email: string;
    phone?: string | null;
    roleName: string;
    active: boolean;
    teamName?: string | null;
    feeBase: number;
    feeProcessing: number;
    feeDiscount: number;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    profileValues: Record<string, string>;
    profileMetadataJson?: string | null;
    accountingRecords: Array<AccountingRecordDto>;
};

