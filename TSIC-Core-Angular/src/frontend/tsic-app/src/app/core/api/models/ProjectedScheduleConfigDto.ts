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
};

