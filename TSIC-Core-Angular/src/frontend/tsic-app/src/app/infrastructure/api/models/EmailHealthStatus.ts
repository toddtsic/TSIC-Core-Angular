/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type EmailHealthStatus = {
    emailingEnabled?: boolean;
    isDevelopment?: boolean;
    sandboxMode?: boolean;
    sesReachable?: boolean;
    max24HourSend?: number;
    sentLast24Hours?: number;
    maxSendRate?: number;
    region?: string;
    warning?: string | null;
};

