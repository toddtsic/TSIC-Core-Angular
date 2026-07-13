/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountReachDto } from './AccountReachDto';
import type { ResetPasswordTarget } from './ResetPasswordTarget';
export type ResetContextDto = {
    target: ResetPasswordTarget;
    userName: string;
    email?: string | null;
    ownerName?: string | null;
    ownerPhone?: string | null;
    isFamilyLogin: boolean;
    signsInFor: Array<AccountReachDto>;
};

