import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';

import { AccessDeniedComponent } from '../../../claim-portal/src/app/access-denied.component';
import { AdjusterWorkbenchComponent } from './adjuster-workbench.component';
import { roleGuard } from './role.guard';

export const routes: Routes = [
    {
        path: '',
        component: AdjusterWorkbenchComponent,
        canActivate: [MsalGuard, roleGuard],
        data: { requiredRole: 'Adjuster' }
    },
    {
        path: 'access-denied',
        component: AccessDeniedComponent
    }
];
