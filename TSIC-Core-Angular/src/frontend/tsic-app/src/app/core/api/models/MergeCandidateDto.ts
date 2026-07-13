/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MergeCandidateChildDto } from './MergeCandidateChildDto';
import type { MergeCandidateRegistrationDto } from './MergeCandidateRegistrationDto';
export type MergeCandidateDto = {
    userName: string;
    userId: string;
    momName?: string | null;
    momEmail?: string | null;
    momPhone?: string | null;
    dadName?: string | null;
    dadEmail?: string | null;
    personName?: string | null;
    email?: string | null;
    phone?: string | null;
    children: Array<MergeCandidateChildDto>;
    registrations: Array<MergeCandidateRegistrationDto>;
};

