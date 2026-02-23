/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { StoreCartLineItemDto } from './StoreCartLineItemDto';
export type StoreFamilyTransactionDto = {
    storeCartBatchId: number;
    purchaseDate: string;
    totalPaid: number;
    itemCount: number;
    items: Array<StoreCartLineItemDto>;
};

