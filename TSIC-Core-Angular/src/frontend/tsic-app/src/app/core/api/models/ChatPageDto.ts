/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChatMessageDto } from './ChatMessageDto';
export type ChatPageDto = {
    messages: Array<ChatMessageDto>;
    nextCursor: number;
    highWaterSeq: number;
    hasMore: boolean;
    hasOlder: boolean;
    prevCursor?: number | null;
    lastReadSeq: number;
    unreadCount: number;
};

