/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProjectedAgegroupConfig } from './ProjectedAgegroupConfig';
import type { ProjectedTimingDefaults } from './ProjectedTimingDefaults';
export type ProjectedScheduleConfigDto = {
    sourceJobId: string;
    sourceJobName: string;
    sourceYear: string;
    agegroups: Array<ProjectedAgegroupConfig>;
    timingDefaults: ProjectedTimingDefaults;
    suggestedWaves?: any | null;
    suggestedOrder?: any[] | null;
    suggestedDivisionWaves?: any | null;
    suggestedDivisionOrder?: any[] | null;
};

