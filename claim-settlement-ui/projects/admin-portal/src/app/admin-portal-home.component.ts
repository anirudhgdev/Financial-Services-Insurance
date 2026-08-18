import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MsalService } from '@azure/msal-angular';

import { getRuntimeAuthConfiguration } from './auth.config';

interface ProviderConfiguration {
    providerId: string; providerName: string; manualReviewFraudThreshold: number; manualReviewClaimAmountThreshold: number;
    deduplicationWindowDays: number; informationRequestDeadlineDays: number; adjusterSlaPeriodHours: number;
    supportedClaimTypes: string; supportedNotificationChannels: string; pipelineConcurrencyLimit: number;
    claimTypeMandatoryFields: string; coverageMappingRules: string; exclusionSets: string; alwaysManualClaimTypes: string; isActive: boolean;
}

interface ProviderUser { userId: string; email?: string; firstAccessedAtUtc: string; lastAccessedAtUtc: string; roles: string[]; }

@Component({
    selector: 'app-admin-portal-home',
    imports: [CommonModule, FormsModule],
    template: `
            <main class="admin-shell">
                <header><p class="eyebrow">Provider administration</p><h1>Configuration and access</h1></header>
                @if (error()) { <p class="error">{{ error() }}</p> }
                <section>
                    <h2>Workflow configuration</h2>
                    @if (configuration(); as config) {
                        <form (ngSubmit)="saveConfiguration(config)">
                            <label>Provider name <input name="providerName" [(ngModel)]="config.providerName" required /></label>
                            <label>Fraud review threshold <input name="fraud" [(ngModel)]="config.manualReviewFraudThreshold" type="number" min="0.3" max="0.9" step="0.01" required /></label>
                            <label>Manual review amount <input name="amount" [(ngModel)]="config.manualReviewClaimAmountThreshold" type="number" min="0" required /></label>
                            <label>Pipeline concurrency <input name="concurrency" [(ngModel)]="config.pipelineConcurrencyLimit" type="number" min="1" required /></label>
                            <label>Supported claim types (JSON) <textarea name="claimTypes" [(ngModel)]="config.supportedClaimTypes" rows="2"></textarea></label>
                            <label><input name="active" [(ngModel)]="config.isActive" type="checkbox" /> Provider active</label>
                            <button type="submit">Save configuration</button>
                        </form>
                    }
                </section>
                <section>
                    <h2>Application access</h2>
                    <p class="note">User identity, MFA, account creation, and account lifecycle remain in your Entra tenant.</p>
                    <table><thead><tr><th>User</th><th>Last access</th><th>Application roles</th><th></th></tr></thead><tbody>
                        @for (user of users(); track user.userId) { <tr>
                            <td>{{ user.email ?? user.userId }}</td><td>{{ user.lastAccessedAtUtc | date:'short' }}</td>
                            <td>@for (role of roles; track role) { <label class="role"><input type="checkbox" [checked]="user.roles.includes(role)" (change)="toggleRole(user, role, $any($event.target).checked)" /> {{ role }}</label> }</td>
                            <td><button type="button" (click)="saveRoles(user)">Save roles</button></td>
                        </tr> }
                    </tbody></table>
                </section>
                <section><h2>Audit export</h2><div class="audit"><label>From <input type="date" [(ngModel)]="auditFrom" /></label><label>To <input type="date" [(ngModel)]="auditTo" /></label><button type="button" (click)="downloadAudit()">Download JSON Lines</button></div></section>
                @if (message()) { <p class="message">{{ message() }}</p> }
            </main>`,
    styles: [`
            .admin-shell{max-width:72rem;margin:2rem auto;padding:0 1rem;font-family:'Segoe UI',sans-serif;color:#183247}.eyebrow{color:#0c6b63;font-size:.75rem;font-weight:700;text-transform:uppercase}section{border-top:1px solid #d7dde2;padding:1.25rem 0}form{display:grid;grid-template-columns:repeat(auto-fit,minmax(14rem,1fr));gap:1rem;align-items:end}label{display:grid;gap:.35rem;font-weight:600}input,textarea{padding:.5rem;border:1px solid #9aa9b3;border-radius:4px;font:inherit}button{background:#0c6b63;border:0;border-radius:4px;color:#fff;padding:.6rem .9rem;font-weight:600;width:max-content}.role{display:inline-flex;grid-template-columns:auto auto;margin-right:.75rem;font-weight:400}.role input{padding:0}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #d7dde2;padding:.75rem;text-align:left;vertical-align:top}.note{color:#637381}.audit{display:flex;gap:1rem;align-items:end}.error{color:#a4262c}.message{color:#0c6b63}@media(max-width:720px){table{display:block;overflow-x:auto}.audit{flex-wrap:wrap}}
        `]
})
export class AdminPortalHomeComponent {
    private readonly msal = inject(MsalService);
    protected readonly configuration = signal<ProviderConfiguration | undefined>(undefined);
    protected readonly users = signal<ProviderUser[]>([]);
    protected readonly error = signal('');
    protected readonly message = signal('');
    protected readonly roles = ['Customer', 'Adjuster', 'ProviderAdmin'];
    protected auditFrom = '';
    protected auditTo = '';

    constructor() { void this.load(); }

    protected toggleRole(user: ProviderUser, role: string, assigned: boolean): void {
        user.roles = assigned ? [...user.roles, role] : user.roles.filter(item => item !== role);
        this.users.update(users => [...users]);
    }

    protected async saveConfiguration(config: ProviderConfiguration): Promise<void> { await this.put(`/api/v1/providers/${this.providerId}/config`, config, 'Configuration saved.'); }
    protected async saveRoles(user: ProviderUser): Promise<void> { await this.put(`/api/v1/providers/${this.providerId}/users/${encodeURIComponent(user.userId)}/roles`, { roles: user.roles }, 'Application roles saved.'); }
    protected async downloadAudit(): Promise<void> {
        try { const params = new URLSearchParams(); if (this.auditFrom) params.set('fromUtc', new Date(this.auditFrom).toISOString()); if (this.auditTo) params.set('toUtc', new Date(`${this.auditTo}T23:59:59Z`).toISOString()); const response = await fetch(`${this.apiBaseUrl}/api/v1/providers/${this.providerId}/audit-log?${params}`, { headers: { Authorization: `Bearer ${await this.accessToken()}` } }); if (!response.ok) throw new Error('Audit export failed.'); const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a'); link.href = url; link.download = `audit-${this.providerId}.jsonl`; link.click(); URL.revokeObjectURL(url); } catch (error) { this.error.set(error instanceof Error ? error.message : 'Audit export failed.'); }
    }

    private async load(): Promise<void> { try { const headers = { Authorization: `Bearer ${await this.accessToken()}` }; const [config, users] = await Promise.all([fetch(`${this.apiBaseUrl}/api/v1/providers/${this.providerId}/config`, { headers }), fetch(`${this.apiBaseUrl}/api/v1/providers/${this.providerId}/users`, { headers })]); if (!config.ok || !users.ok) throw new Error('Unable to load provider administration data.'); this.configuration.set(await config.json() as ProviderConfiguration); this.users.set(await users.json() as ProviderUser[]); } catch (error) { this.error.set(error instanceof Error ? error.message : 'Unable to load provider administration data.'); } }
    private async put(path: string, body: unknown, successMessage: string): Promise<void> { try { const response = await fetch(`${this.apiBaseUrl}${path}`, { method: 'PUT', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${await this.accessToken()}` }, body: JSON.stringify(body) }); if (!response.ok) throw new Error((await response.json().catch(() => ({ error: 'Save failed.' }))).error); this.message.set(successMessage); } catch (error) { this.error.set(error instanceof Error ? error.message : 'Save failed.'); } }
    private get providerId(): string { return this.msal.instance.getActiveAccount()?.tenantId ?? this.msal.instance.getAllAccounts()[0]?.tenantId ?? ''; }
    private get apiBaseUrl(): string { return getRuntimeAuthConfiguration()?.apiBaseUrl?.replace(/\/$/, '') ?? ''; }
    private async accessToken(): Promise<string> { const account = this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0]; if (!account) throw new Error('Sign in is required to administer a provider.'); const scope = getRuntimeAuthConfiguration()?.apiScope; return (await this.msal.instance.acquireTokenSilent({ account, scopes: scope ? [scope] : ['openid', 'profile'] })).accessToken; }
}