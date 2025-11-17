/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreditCardInfo } from './CreditCardInfo';
import type { PaymentOption } from './PaymentOption';
export type PaymentRequestDto = {
    jobId?: string;
    familyUserId?: string;
    paymentOption?: PaymentOption;
    creditCard?: CreditCardInfo;
    idempotencyKey?: string;
    viConfirmed?: boolean;
    viPolicyNumber?: string;
    viPolicyCreateDate?: string;
};

