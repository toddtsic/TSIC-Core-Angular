/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CcInfoDto } from './CcInfoDto';
import type { FamilyPlayerDto } from './FamilyPlayerDto';
import type { FamilyUserSummaryDto } from './FamilyUserSummaryDto';
import type { JobRegFormDto } from './JobRegFormDto';
import type { RegSaverDetailsDto } from './RegSaverDetailsDto';
export type FamilyPlayersResponseDto = {
    familyUser: FamilyUserSummaryDto;
    familyPlayers: Array<FamilyPlayerDto>;
    regSaverDetails?: (null | RegSaverDetailsDto);
    jobRegForm?: (null | JobRegFormDto);
    ccInfo?: (null | CcInfoDto);
    jobHasActiveDiscountCodes: boolean;
    jobUsesAmex: boolean;
};

