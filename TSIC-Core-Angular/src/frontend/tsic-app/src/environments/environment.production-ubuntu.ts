// Production build for an Ubuntu server (scripts/Ubuntu-Config).
// runtimeConfig: the two addresses below are placeholders. main.ts replaces them at
// start-up from app-config.json, which publish.sh writes from the target machine's
// settings, so one build runs on any machine.
export const environment = {
    envName: 'production',
    production: true,
    runtimeConfig: true,
    apiUrl: '',
    staticsUrl: '',
    testUsLaxNumber: null as string | null,
    buildVersion: 'dev',
};
