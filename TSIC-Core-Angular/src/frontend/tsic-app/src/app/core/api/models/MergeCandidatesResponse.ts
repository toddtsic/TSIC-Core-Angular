/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MergeCandidateDto } from './MergeCandidateDto';
import type { MergeIdentityDto } from './MergeIdentityDto';
export type MergeCandidatesResponse = {
    identity?: (null | MergeIdentityDto);
    accounts: Array<MergeCandidateDto>;
    roleName?: string | null;
};

