/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type CreateAccountingRecordRequest = {
    registrationId: string;
    paymentMethodId: string;
    dueAmount?: number;
    paidAmount?: number;
    comment?: string | null;
    checkNo?: string | null;
    promoCode?: string | null;
};

