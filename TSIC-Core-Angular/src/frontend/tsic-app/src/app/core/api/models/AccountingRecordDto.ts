/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AccountingRecordDto = {
    aId: number;
    date: string | null;
    paymentMethod: string;
    dueAmount: number;
    paidAmount: number;
    comment?: string | null;
    checkNo?: string | null;
    promoCode?: string | null;
    active?: boolean | null;
    adnTransactionId?: string | null;
    adnCc4?: string | null;
    adnCcExpDate?: string | null;
    adnInvoiceNo?: string | null;
    canRefund?: boolean;
};

