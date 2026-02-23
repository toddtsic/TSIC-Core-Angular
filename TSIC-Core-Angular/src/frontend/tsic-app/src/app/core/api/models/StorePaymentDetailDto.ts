/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type StorePaymentDetailDto = {
    storeCartBatchAccountingId: number;
    storeCartBatchId: number;
    familyUserId: string;
    familyUserName: string;
    paymentMethodName: string;
    paid: number;
    createDate: string;
    cclast4?: string | null;
    adnInvoiceNo?: string | null;
    adnTransactionId?: string | null;
    comment?: string | null;
    isWalkUp: boolean;
};

