/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type StoreSaleLineDto = {
    storeCartBatchSkuId: number;
    storeCartBatchId: number;
    storeSkuId: number;
    active: boolean;
    familyUserName: string;
    itemName: string;
    skuLabel: string;
    quantity: number;
    unitPrice: number;
    feeProduct: number;
    feeProcessing: number;
    salesTax: number;
    feeTotal: number;
    paid: number;
    refunded: number;
    maxCanRefund: number;
    restocked: number;
    maxCanRestock: number;
    purchaseDate?: string | null;
    modifiedDate: string;
    isWalkUp: boolean;
    directToFirstName?: string | null;
    directToLastName?: string | null;
    directToEmail?: string | null;
    directToCellphone?: string | null;
    directToClub?: string | null;
    directToAgegroup?: string | null;
    directToPool?: string | null;
    directToTeam?: string | null;
};

