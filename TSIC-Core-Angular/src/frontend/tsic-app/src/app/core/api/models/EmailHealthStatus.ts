/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type EmailHealthStatus = {
    emailingEnabled?: boolean;
    isDevelopment?: boolean;
    sandboxMode?: boolean;
    sesReachable?: boolean;
    max24HourSend?: number | null;
    sentLast24Hours?: number | null;
    maxSendRate?: number | null;
    region?: string | null;
    warning?: string | null;
};

