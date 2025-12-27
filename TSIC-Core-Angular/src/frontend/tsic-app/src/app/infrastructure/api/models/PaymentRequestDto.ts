/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreditCardInfo } from './CreditCardInfo';
import type { PaymentOption } from './PaymentOption';
export type PaymentRequestDto = {
    jobId: string;
    familyUserId: string;
    paymentOption: PaymentOption;
    creditCard?: (null | CreditCardInfo);
    idempotencyKey?: string | null;
    viConfirmed?: boolean | null;
    viPolicyNumber?: string | null;
    viPolicyCreateDate?: string | null;
    viQuoteIds?: any[] | null;
    viToken?: string | null;
};

