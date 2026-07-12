/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MergeCandidateChildDto } from './MergeCandidateChildDto';
export type MergeCandidateDto = {
    userName: string;
    userId: string;
    registrationCount: number;
    email?: string | null;
    personName?: string | null;
    dob?: string | null;
    momName?: string | null;
    momEmail?: string | null;
    dadName?: string | null;
    dadEmail?: string | null;
    children: Array<MergeCandidateChildDto>;
    jobs: Array<string>;
};

