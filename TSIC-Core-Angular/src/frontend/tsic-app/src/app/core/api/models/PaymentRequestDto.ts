/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BankAccountInfo } from './BankAccountInfo';
import type { CreditCardInfo } from './CreditCardInfo';
import type { PaymentOption } from './PaymentOption';
export type PaymentRequestDto = {
    jobPath: string;
    paymentOption: PaymentOption;
    creditCard?: (null | CreditCardInfo);
    bankAccount?: (null | BankAccountInfo);
    idempotencyKey?: string | null;
    viConfirmed?: boolean | null;
    viPolicyNumber?: string | null;
    viPolicyCreateDate?: string | null;
    viQuoteIds?: any[] | null;
    viToken?: string | null;
};

