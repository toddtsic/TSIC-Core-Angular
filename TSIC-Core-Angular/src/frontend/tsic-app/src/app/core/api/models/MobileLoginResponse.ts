/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MobileContextDto } from './MobileContextDto';
import type { MobileOwnershipDto } from './MobileOwnershipDto';
export type MobileLoginResponse = {
    accessToken: string;
    refreshToken: string;
    expiresIn: number;
    requiresTosSignature: boolean;
    autoResolved: boolean;
    hasExpiredRegistrations: boolean;
    contexts: Array<MobileContextDto>;
    ownerships: Array<MobileOwnershipDto>;
};

