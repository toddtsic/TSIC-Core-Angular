/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DayOfWeek } from './DayOfWeek';
import type { FieldUsageDto } from './FieldUsageDto';
import type { RoundShapeDto } from './RoundShapeDto';
import type { TimeRangeDto } from './TimeRangeDto';
export type DivisionSizeProfile = {
    tCnt: number;
    divisionCount: number;
    playDays: Array<DayOfWeek>;
    startOffsetFromWindow?: any | null;
    timeRangeAbsolute: Record<string, TimeRangeDto>;
    windowUtilization?: any | null;
    fieldBand: Array<string>;
    roundCount: number;
    gameGuarantee: number;
    placementShapePerRound: Record<string, RoundShapeDto>;
    onsiteIntervalPerDay: Record<string, string>;
    fieldDesirability: Record<string, FieldUsageDto>;
    roundsPerDay: Record<string, number>;
    extraRoundDay?: (null | DayOfWeek);
    interRoundInterval: string;
};

