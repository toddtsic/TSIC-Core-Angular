/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProjectedGameDay } from './ProjectedGameDay';
export type ProjectedAgegroupConfig = {
    agegroupId: string;
    agegroupName: string;
    gameDays: Array<ProjectedGameDay>;
    fieldsByDay: Record<string, Array<string>>;
    gsi: number;
    startTime: string;
    maxGamesPerField: number;
};

