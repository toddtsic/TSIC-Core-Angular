/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
import type { RegisteredTeamDto } from './RegisteredTeamDto';
export type FamilyAccountingDto = {
    anchorRegistrationId: string;
    familyName: string;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    players: Array<RegisteredTeamDto>;
    accountingRecords: Array<AccountingRecordDto>;
    pendingPlayerRegistrationIds: Array<string>;
};

