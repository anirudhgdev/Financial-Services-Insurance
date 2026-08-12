import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import {
  MsalBroadcastService,
  MsalGuard,
  MsalGuardConfiguration,
  MsalService,
  MSAL_GUARD_CONFIG,
  MSAL_INSTANCE
} from '@azure/msal-angular';
import { InteractionType } from '@azure/msal-browser';

import { routes } from './app.routes';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { createMsalInstance } from './auth.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideClientHydration(withEventReplay()),
    { provide: MSAL_INSTANCE, useFactory: createMsalInstance },
    {
      provide: MSAL_GUARD_CONFIG,
      useValue: {
        interactionType: InteractionType.Redirect,
        authRequest: { scopes: ['openid', 'profile'] }
      } satisfies MsalGuardConfiguration
    },
    MsalService,
    MsalBroadcastService,
    MsalGuard
  ]
};
