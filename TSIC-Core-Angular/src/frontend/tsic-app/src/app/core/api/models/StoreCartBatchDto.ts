/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { StoreCartLineItemDto } from './StoreCartLineItemDto';
export type StoreCartBatchDto = {
    storeCartBatchId: number;
    lineItems: Array<StoreCartLineItemDto>;
    subtotal: number;
    totalFees: number;
    totalTax: number;
    grandTotal: number;
};

