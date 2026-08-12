import { Configuration, PublicClientApplication } from '@azure/msal-browser';

export interface RuntimeAuthConfiguration {
    clientId: string;
    tenantId: string;
    redirectUri?: string;
    apiBaseUrl?: string;
    apiScope?: string;
}

declare global {
    interface Window {
        __claimSettlementAuth?: RuntimeAuthConfiguration;
    }
}

export function createMsalInstance(): PublicClientApplication {
    const runtimeConfiguration =
        typeof window === 'undefined' ? undefined : window.__claimSettlementAuth;

    const configuration: Configuration = {
        auth: {
            clientId: runtimeConfiguration?.clientId ?? '',
            authority: runtimeConfiguration?.tenantId
                ? `https://login.microsoftonline.com/${runtimeConfiguration.tenantId}`
                : undefined,
            redirectUri: runtimeConfiguration?.redirectUri ?? '/'
        }
    };

    return new PublicClientApplication(configuration);
}

export function getRuntimeAuthConfiguration(): RuntimeAuthConfiguration | undefined {
    return typeof window === 'undefined' ? undefined : window.__claimSettlementAuth;
}