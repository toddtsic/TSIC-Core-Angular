/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FamilyPlayerDto } from './FamilyPlayerDto';
import type { FamilyUserSummaryDto } from './FamilyUserSummaryDto';
import type { JobRegFormDto } from './JobRegFormDto';
import type { RegSaverDetailsDto } from './RegSaverDetailsDto';
import type { CcInfoDto } from './CcInfoDto';
export type FamilyPlayersResponseDto = {
    familyUser?: FamilyUserSummaryDto;
    familyPlayers?: Array<FamilyPlayerDto> | null;
    regSaverDetails?: RegSaverDetailsDto;
    jobRegForm?: JobRegFormDto;
    ccInfo?: CcInfoDto;
    jobHasActiveDiscountCodes?: boolean;
    jobUsesAmex?: boolean;
};

