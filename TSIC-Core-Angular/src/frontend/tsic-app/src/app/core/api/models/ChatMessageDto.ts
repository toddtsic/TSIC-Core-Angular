/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ChatMessageDto = {
    messageId: string;
    seq: number;
    lastTouchSeq: number;
    kind: number;
    text: string;
    authorName: string;
    authorHeadshotUrl?: string | null;
    clientMessageId: string;
    replyToMessageId?: string | null;
    created: string;
    editedAt?: string | null;
    isDeleted: boolean;
    pinnedAt?: string | null;
};

