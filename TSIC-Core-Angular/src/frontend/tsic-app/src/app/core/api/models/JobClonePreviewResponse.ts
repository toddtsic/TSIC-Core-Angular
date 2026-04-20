/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupPreviewDto } from './AgegroupPreviewDto';
import type { BulletinShiftDto } from './BulletinShiftDto';
import type { DateShiftDto } from './DateShiftDto';
import type { FeeModifierShiftDto } from './FeeModifierShiftDto';
export type JobClonePreviewResponse = {
    yearDelta: number;
    inferredLeagueName: string;
    currentProcessingFeePercent: number;
    sourceProcessingFeePercent?: number | null;
    eventStartShift?: (null | DateShiftDto);
    eventEndShift?: (null | DateShiftDto);
    adnArbStartShift?: (null | DateShiftDto);
    adminsToDeactivate: number;
    adminsPreserved: number;
    bulletins?: Array<BulletinShiftDto>;
    agegroups?: Array<AgegroupPreviewDto>;
    feeModifiers?: Array<FeeModifierShiftDto>;
};

