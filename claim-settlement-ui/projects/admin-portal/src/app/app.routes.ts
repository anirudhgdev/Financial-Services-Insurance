import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';

import { AccessDeniedComponent } from '../../../claim-portal/src/app/access-denied.component';
import { AdminPortalHomeComponent } from './admin-portal-home.component';
import { roleGuard } from './role.guard';

export const routes: Routes = [
    {
        path: '',
        component: AdminPortalHomeComponent,
        canActivate: [MsalGuard, roleGuard],
        data: { requiredRole: 'ProviderAdmin' }
    },
    {
        path: 'access-denied',
        component: AccessDeniedComponent
    }
];
