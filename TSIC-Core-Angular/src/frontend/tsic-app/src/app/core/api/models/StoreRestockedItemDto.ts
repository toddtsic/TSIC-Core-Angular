/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type StoreRestockedItemDto = {
    storeCartBatchSkuRestockId: number;
    storeCartBatchId: number;
    storeCartBatchSkuId: number;
    itemName: string;
    colorName?: string | null;
    sizeName?: string | null;
    skuQuantity: number;
    restockCount: number;
    paidTotal: number;
    refundedTotal: number;
    purchaseDate: string;
    familyUserName: string;
    directToPlayerName?: string | null;
    modifiedDate: string;
    modifiedBy: string;
};

