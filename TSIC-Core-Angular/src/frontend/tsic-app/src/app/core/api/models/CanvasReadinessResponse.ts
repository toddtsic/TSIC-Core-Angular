/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupCanvasReadinessDto } from './AgegroupCanvasReadinessDto';
import type { EventFieldSummaryDto } from './EventFieldSummaryDto';
import type { PriorYearFieldDefaults } from './PriorYearFieldDefaults';
export type CanvasReadinessResponse = {
    agegroups: Array<AgegroupCanvasReadinessDto>;
    assignedFieldCount: number;
    priorYearDefaults?: (null | PriorYearFieldDefaults);
    priorYearRounds?: any | null;
    eventFields: Array<EventFieldSummaryDto>;
};

