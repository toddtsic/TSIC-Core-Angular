/* generated using openapi-typescript-codegen -- do not edit */
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
    region?: string;
    warning?: string | null;
};

