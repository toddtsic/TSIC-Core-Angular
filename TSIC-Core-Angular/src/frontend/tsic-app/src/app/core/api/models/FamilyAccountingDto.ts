/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
import type { FamilyPlayerSubscriptionDto } from './FamilyPlayerSubscriptionDto';
import type { RegisteredPlayerLineDto } from './RegisteredPlayerLineDto';
export type FamilyAccountingDto = {
    anchorRegistrationId: string;
    familyName: string;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    players: Array<RegisteredPlayerLineDto>;
    accountingRecords: Array<AccountingRecordDto>;
    subscriptions: Array<FamilyPlayerSubscriptionDto>;
    paymentMethodsAllowedCode?: number;
};

