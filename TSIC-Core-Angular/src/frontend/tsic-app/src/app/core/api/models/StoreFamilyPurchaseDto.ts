/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { StoreFamilyTransactionDto } from './StoreFamilyTransactionDto';
export type StoreFamilyPurchaseDto = {
    familyUserId: string;
    familyUserName: string;
    transactions: Array<StoreFamilyTransactionDto>;
    totalSpent: number;
};

