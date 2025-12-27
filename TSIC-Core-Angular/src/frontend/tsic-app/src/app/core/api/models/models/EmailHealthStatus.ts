/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type EmailHealthStatus = {
    emailingEnabled?: boolean;
    isDevelopment?: boolean;
    sandboxMode?: boolean;
    sesReachable?: boolean;
    max24HourSend?: number | string | null;
    sentLast24Hours?: number | string | null;
    maxSendRate?: number | string | null;
    region?: string;
    warning?: string | null;
};

