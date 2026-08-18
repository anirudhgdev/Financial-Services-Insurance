import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, DestroyRef, PLATFORM_ID, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MsalService } from '@azure/msal-angular';

import { getRuntimeAuthConfiguration } from './auth.config';

interface ClaimStatus {
    claimId: string;
    status: string;
    currentStage: string;
    completedStages: string[];
    estimatedMinutesRemaining: number;
    statusMessage: string;
    updatedAtUtc: string;
}

@Component({
    selector: 'app-claim-status-tracker',
    imports: [CommonModule, RouterLink],
    template: `
    <main class="status-shell">
      <a routerLink="/">Back to claim intake</a>
      <header><p class="eyebrow">Claim status</p><h1>Claim {{ claimId() }}</h1></header>
      @if (error()) { <p class="error">{{ error() }}</p> }
      @if (status(); as claim) {
        <section class="status-summary" aria-live="polite">
          <p class="status">{{ claim.statusMessage }}</p>
          <p><strong>Current stage:</strong> {{ claim.currentStage }}</p>
          <p><strong>Estimated time remaining:</strong> {{ remainingTime(claim.estimatedMinutesRemaining) }}</p>
          <p class="updated">Updated {{ claim.updatedAtUtc | date:'short' }}</p>
        </section>
        <ol class="stages">
          @for (stage of stages; track stage) {
            <li [class.complete]="claim.completedStages.includes(stage)" [class.active]="claim.currentStage === stage">{{ stage }}</li>
          }
        </ol>
      } @else if (!error()) { <p>Loading your claim status...</p> }
    </main>
  `,
    styles: [`
    .status-shell { max-width: 46rem; margin: 3rem auto; padding: 0 1rem; color: #183247; font-family: 'Segoe UI', sans-serif; }
    a { color: #0c6b63; font-weight: 600; } .eyebrow { color: #0c6b63; font-size: .75rem; font-weight: 700; text-transform: uppercase; }
    h1 { margin: .25rem 0 1.5rem; } .status-summary { border: 1px solid #d7dde2; border-left: 4px solid #0c6b63; padding: 1rem 1.25rem; }
    .status { font-size: 1.15rem; font-weight: 600; } .updated { color: #637381; font-size: .875rem; } .stages { display: grid; gap: .75rem; margin: 1.5rem 0; padding: 0; list-style: none; }
    .stages li { border-left: 3px solid #d7dde2; padding: .65rem 1rem; } .stages li.complete { border-color: #0c6b63; } .stages li.active { background: #eef4f2; border-color: #c94c23; font-weight: 700; }
    .error { color: #a4262c; } @media (max-width: 720px) { .status-shell { margin-top: 1rem; } }
  `]
})
export class ClaimStatusTrackerComponent {
    private readonly msal = inject(MsalService);
    private readonly route = inject(ActivatedRoute);
    private readonly platformId = inject(PLATFORM_ID);
    private readonly destroyRef = inject(DestroyRef);
    protected readonly status = signal<ClaimStatus | undefined>(undefined);
    protected readonly error = signal('');
    protected readonly claimId = signal('');
    protected readonly stages = ['Document analysis', 'Policy validation', 'Fraud assessment', 'Settlement decision', 'Human review'];

    constructor() {
        const claimId = this.route.snapshot.paramMap.get('claimId') ?? '';
        this.claimId.set(claimId);
        if (!isPlatformBrowser(this.platformId) || !claimId) return;
        void this.loadStatus();
        const poller = window.setInterval(() => void this.loadStatus(), 15000);
        this.destroyRef.onDestroy(() => window.clearInterval(poller));
    }

    protected remainingTime(minutes: number): string {
        if (minutes === 0) return 'Complete';
        if (minutes >= 1440) return `${Math.ceil(minutes / 1440)} business day(s)`;
        return `About ${minutes} minute(s)`;
    }

    private async loadStatus(): Promise<void> {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/v1/claims/${this.claimId()}/status`, {
                headers: { Authorization: `Bearer ${await this.accessToken()}` }
            });
            if (!response.ok) throw new Error('Unable to load this claim status.');
            this.status.set(await response.json() as ClaimStatus);
            this.error.set('');
        } catch (error) {
            this.error.set(error instanceof Error ? error.message : 'Unable to load this claim status.');
        }
    }

    private get apiBaseUrl(): string { return getRuntimeAuthConfiguration()?.apiBaseUrl?.replace(/\/$/, '') ?? ''; }
    private async accessToken(): Promise<string> {
        const account = this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0];
        if (!account) throw new Error('Sign in is required to view a claim.');
        const scope = getRuntimeAuthConfiguration()?.apiScope;
        return (await this.msal.instance.acquireTokenSilent({ account, scopes: scope ? [scope] : ['openid', 'profile'] })).accessToken;
    }
}