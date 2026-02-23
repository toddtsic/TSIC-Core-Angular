/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CadtClubNode } from './CadtClubNode';
import type { FieldSummaryDto } from './FieldSummaryDto';
import type { LadtAgegroupNode } from './LadtAgegroupNode';
export type ScheduleFilterOptionsDto = {
    clubs: Array<CadtClubNode>;
    agegroups: Array<LadtAgegroupNode>;
    gameDays: Array<string>;
    times: Array<string>;
    fields: Array<FieldSummaryDto>;
};

