/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FamilyProfileResponse } from './FamilyProfileResponse';
export type ValidateCredentialsResponse = {
    exists: boolean;
    profile?: (null | FamilyProfileResponse);
    message?: string | null;
    accessToken?: string | null;
};

