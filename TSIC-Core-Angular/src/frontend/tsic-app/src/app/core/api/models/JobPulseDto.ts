/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
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

