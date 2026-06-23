/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SupersedingEventInfoDto } from './SupersedingEventInfoDto';
export type JobPulseDto = {
    playerRegistrationOpen: boolean;
    playerTeamsAvailableForRegistration: boolean;
    playerRegRequiresToken: boolean;
    teamRegistrationOpen: boolean;
    teamRegRequiresToken: boolean;
    clubRepAllowAdd: boolean;
    clubRepAllowEdit: boolean;
    clubRepAllowDelete: boolean;
    allowRosterViewPlayer: boolean;
    allowRosterViewAdult: boolean;
    publicRostersAvailable: boolean;
    offerPlayerRegsaverInsurance: boolean;
    offerTeamRegsaverInsurance: boolean;
    storeEnabled: boolean;
    storeHasActiveItems: boolean;
    allowStoreWalkup: boolean;
    enableStayToPlay: boolean;
    schedulePublished: boolean;
    playerRegistrationPlanned: boolean;
    adultRegistrationPlanned: boolean;
    publicSuspended: boolean;
    registrationExpiry?: string | null;
    playerRegClosesSoonest?: string | null;
    playerRegOpensSoonest?: string | null;
    firstGameDate?: string | null;
    lastGameDate?: string | null;
    supersededByLaterEvent?: (null | SupersedingEventInfoDto);
    myAssignedTeamId?: string | null;
    myRegistrationOwedTotal?: number | null;
    myHasPurchasedPlayerRegsaver?: boolean | null;
    myAdnSubscriptionId?: string | null;
    myClubRepTeamCount?: number | null;
    myClubRepTotalOwed?: number | null;
    myClubRepHasTeamWithoutRegsaver?: boolean | null;
    myFirstName?: string | null;
    myLastName?: string | null;
};

