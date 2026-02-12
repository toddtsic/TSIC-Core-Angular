/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
import type { FamilyContactDto } from './FamilyContactDto';
import type { UserDemographicsDto } from './UserDemographicsDto';
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
    momLabel?: string;
    dadLabel?: string;
    familyContact?: (null | FamilyContactDto);
    userDemographics?: (null | UserDemographicsDto);
    registrationDate?: string | null;
    modifiedDate?: string | null;
    accountingRecords: Array<AccountingRecordDto>;
};

