/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type StoreCartLineItemDto = {
    storeCartBatchSkuId: number;
    storeSkuId: number;
    itemName: string;
    colorName?: string | null;
    sizeName?: string | null;
    quantity: number;
    unitPrice: number;
    feeProduct: number;
    feeProcessing: number;
    salesTax: number;
    feeTotal: number;
    lineTotal: number;
    directToRegId?: string | null;
    directToPlayerName?: string | null;
    active: boolean;
};

