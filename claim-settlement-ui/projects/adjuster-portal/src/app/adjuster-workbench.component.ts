import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MsalService } from '@azure/msal-angular';

import { getRuntimeAuthConfiguration } from './auth.config';

interface QueueClaim {
    claimId: string;
    claimantId: string;
    claimType: string;
    status: string;
    priority: string;
    assignedAdjusterId?: string;
}

interface ReviewPackage {
    claimId: string;
    claimSummary: string;
    policyValidationSummary: string;
    fraudSummary: string;
    documentHighlights: string;
    settlementReasoning: string;
    recommendedSettlementAmount: number;
    missingSections: string[];
}

interface QueueResponse {
    page: number;
    pageSize: number;
    totalCount: number;
    claims: QueueClaim[];
}

@Component({
    selector: 'app-adjuster-workbench',
    imports: [CommonModule, FormsModule],
    templateUrl: './app.html',
    styleUrl: './app.scss'
})
export class AdjusterWorkbenchComponent {
    private readonly msal = inject(MsalService);
    protected readonly title = signal('Adjuster Workbench');
    protected readonly queue = signal<QueueClaim[]>([]);
    protected readonly selectedClaim = signal<QueueClaim | undefined>(undefined);
    protected readonly reviewPackage = signal<ReviewPackage | undefined>(undefined);
    protected readonly error = signal('');
    protected readonly page = signal(1);
    protected readonly totalCount = signal(0);
    protected readonly pageSize = 25;
    protected decision: 'APPROVE' | 'REJECT' | 'ESCALATE' = 'APPROVE';
    protected rationale = '';
    protected settlementOverride: number | null = null;
    protected message = '';

    constructor() {
        void this.loadQueue();
    }

    protected selectClaim(claimId: string): void {
        this.selectedClaim.set(this.queue().find((claim) => claim.claimId === claimId));
        this.reviewPackage.set(undefined);
        this.message = '';
        void this.loadReviewPackage(claimId);
    }

    protected async submitDecision(): Promise<void> {
        if (this.rationale.trim().length < 20) {
            this.message = 'Rationale must be at least 20 characters.';
            return;
        }

        const claim = this.selectedClaim();
        if (!claim) return;

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/v1/claims/${claim.claimId}/adjuster-decision`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${await this.accessToken()}` },
                body: JSON.stringify({ decision: this.decision, rationale: this.rationale, settlementOverride: this.settlementOverride })
            });
            if (!response.ok) throw new Error((await response.json().catch(() => ({ error: 'Decision submission failed.' }))).error);
            this.message = `Decision ${this.decision} submitted for ${claim.claimId}.`;
            this.rationale = '';
            this.settlementOverride = null;
            void this.loadQueue();
        } catch (error) {
            this.message = error instanceof Error ? error.message : 'Decision submission failed.';
        }
    }

    protected changePage(nextPage: number): void {
        if (nextPage < 1 || (nextPage - 1) * this.pageSize >= this.totalCount()) return;
        this.page.set(nextPage);
        this.selectedClaim.set(undefined);
        this.reviewPackage.set(undefined);
        void this.loadQueue();
    }

    private async loadQueue(): Promise<void> {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/v1/claims/adjuster-queue?page=${this.page()}&pageSize=${this.pageSize}`, { headers: { Authorization: `Bearer ${await this.accessToken()}` } });
            if (!response.ok) throw new Error('Unable to load claim queue.');
            const payload = await response.json() as QueueResponse;
            this.queue.set(payload.claims);
            this.totalCount.set(payload.totalCount);
            if (!this.selectedClaim() && payload.claims[0]) this.selectClaim(payload.claims[0].claimId);
        } catch (error) {
            this.error.set(error instanceof Error ? error.message : 'Unable to load claim queue.');
        }
    }

    private async loadReviewPackage(claimId: string): Promise<void> {
        try {
            const response = await fetch(`${this.apiBaseUrl}/api/v1/claims/${claimId}/review-package`, { headers: { Authorization: `Bearer ${await this.accessToken()}` } });
            if (!response.ok) throw new Error('AI review package is unavailable.');
            this.reviewPackage.set(await response.json() as ReviewPackage);
        } catch (error) {
            this.error.set(error instanceof Error ? error.message : 'AI review package is unavailable.');
        }
    }

    private get apiBaseUrl(): string {
        return getRuntimeAuthConfiguration()?.apiBaseUrl?.replace(/\/$/, '') ?? '';
    }

    private async accessToken(): Promise<string> {
        const account = this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0];
        if (!account) throw new Error('Sign in is required to review claims.');
        const scope = getRuntimeAuthConfiguration()?.apiScope;
        return (await this.msal.instance.acquireTokenSilent({ account, scopes: scope ? [scope] : ['openid', 'profile'] })).accessToken;
    }
}