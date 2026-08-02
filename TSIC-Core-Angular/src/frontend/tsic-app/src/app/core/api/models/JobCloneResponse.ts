/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ClonePlanStepDto } from './ClonePlanStepDto';
export type JobCloneResponse = {
    newJobId: string;
    newJobPath: string;
    newJobName: string;
    steps: Array<ClonePlanStepDto>;
    newSuperUserRegistrationId: string;
};

