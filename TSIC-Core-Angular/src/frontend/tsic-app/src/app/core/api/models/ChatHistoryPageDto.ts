/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChatMessageDto } from './ChatMessageDto';
export type ChatHistoryPageDto = {
    messages: Array<ChatMessageDto>;
    prevCursor?: number | null;
    hasOlder: boolean;
};

