/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegistrationFlagsDto } from './RegistrationFlagsDto';
import type { VerifySectionDto } from './VerifySectionDto';
export type JobVerifyChecklistDto = {
    jobId: string;
    jobPath: string;
    jobName: string;
    jobTypeId: number;
    jobTypeName?: string | null;
    bSuspendPublic: boolean;
    registrationFlags: RegistrationFlagsDto;
    sections: Array<VerifySectionDto>;
};

