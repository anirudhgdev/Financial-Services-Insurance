import { isPlatformBrowser } from '@angular/common';
import { inject, PLATFORM_ID } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { MsalService } from '@azure/msal-angular';

export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
    const platformId = inject(PLATFORM_ID);
    if (!isPlatformBrowser(platformId)) {
        return true;
    }

    const requiredRole = route.data['requiredRole'] as string | undefined;
    const msalService = inject(MsalService);
    const router = inject(Router);
    const account = msalService.instance.getActiveAccount() ?? msalService.instance.getAllAccounts()[0];
    const roles = account?.idTokenClaims?.['roles'];

    return !requiredRole || (Array.isArray(roles) && roles.includes(requiredRole))
        ? true
        : router.parseUrl('/access-denied');
};