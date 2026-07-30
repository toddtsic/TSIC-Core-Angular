/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UsLaxEmailRecipientDto } from './UsLaxEmailRecipientDto';
export type UsLaxTestSendRequest = {
    subject: string;
    body: string;
    recipient: UsLaxEmailRecipientDto;
    includeSuperusers?: boolean;
    extraRecipient?: string | null;
};

