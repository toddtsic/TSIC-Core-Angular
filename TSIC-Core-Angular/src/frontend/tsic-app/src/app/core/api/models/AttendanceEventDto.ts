/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AttendanceEventDto = {
    eventId: number;
    teamId: string;
    comment?: string | null;
    eventTypeId: number;
    eventType?: string | null;
    eventDate: string;
    eventLocation?: string | null;
    present?: number;
    notPresent?: number;
    unknown?: number;
    creatorUserId?: string | null;
};

