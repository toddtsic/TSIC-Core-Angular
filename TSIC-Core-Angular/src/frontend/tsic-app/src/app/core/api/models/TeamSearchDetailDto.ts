/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
import type { ClubTeamSummaryDto } from './ClubTeamSummaryDto';
export type TeamSearchDetailDto = {
    teamId: string;
    teamName: string;
    clubName?: string | null;
    agegroupName: string;
    divName?: string | null;
    levelOfPlay?: string | null;
    active: boolean;
    feeBase: number;
    feeProcessing: number;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    teamComments?: string | null;
    clubRepRegistrationId?: string | null;
    clubRepName?: string | null;
    clubRepEmail?: string | null;
    clubRepCellphone?: string | null;
    accountingRecords: Array<AccountingRecordDto>;
    clubTeamSummaries: Array<ClubTeamSummaryDto>;
};

