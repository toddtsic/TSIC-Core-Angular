/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegistrationSearchRequest } from './RegistrationSearchRequest';
export type EmailPreviewRequest = {
    registrationIds: Array<string>;
    criteria?: (null | RegistrationSearchRequest);
    subject: string;
    bodyTemplate: string;
};

