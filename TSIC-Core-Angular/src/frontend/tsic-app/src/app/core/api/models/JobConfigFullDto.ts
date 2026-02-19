/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { JobConfigBrandingDto } from './JobConfigBrandingDto';
import type { JobConfigCoachesDto } from './JobConfigCoachesDto';
import type { JobConfigCommunicationsDto } from './JobConfigCommunicationsDto';
import type { JobConfigGeneralDto } from './JobConfigGeneralDto';
import type { JobConfigMobileStoreDto } from './JobConfigMobileStoreDto';
import type { JobConfigPaymentDto } from './JobConfigPaymentDto';
import type { JobConfigPlayerDto } from './JobConfigPlayerDto';
import type { JobConfigSchedulingDto } from './JobConfigSchedulingDto';
import type { JobConfigTeamsDto } from './JobConfigTeamsDto';
export type JobConfigFullDto = {
    general: JobConfigGeneralDto;
    payment: JobConfigPaymentDto;
    communications: JobConfigCommunicationsDto;
    player: JobConfigPlayerDto;
    teams: JobConfigTeamsDto;
    coaches: JobConfigCoachesDto;
    scheduling: JobConfigSchedulingDto;
    mobileStore: JobConfigMobileStoreDto;
    branding: JobConfigBrandingDto;
};

