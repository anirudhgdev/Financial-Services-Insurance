import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';

import { AccessDeniedComponent } from './access-denied.component';
import { ClaimIntakeComponent } from './claim-intake.component';
import { ClaimStatusTrackerComponent } from './claim-status-tracker.component';
import { roleGuard } from './role.guard';

export const routes: Routes = [
    {
        path: '',
        component: ClaimIntakeComponent,
        canActivate: [MsalGuard, roleGuard],
        data: { requiredRole: 'Customer' }
    },
    {
        path: 'claims/:claimId',
        component: ClaimStatusTrackerComponent,
        canActivate: [MsalGuard, roleGuard],
        data: { requiredRole: 'Customer' }
    },
    {
        path: 'access-denied',
        component: AccessDeniedComponent
    }
];
