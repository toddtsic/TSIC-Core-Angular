/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
import type { RegisteredTeamDto } from './RegisteredTeamDto';
export type ClubRepAccountingDto = {
    clubRepRegistrationId: string;
    clubName: string;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    teams: Array<RegisteredTeamDto>;
    accountingRecords: Array<AccountingRecordDto>;
};

