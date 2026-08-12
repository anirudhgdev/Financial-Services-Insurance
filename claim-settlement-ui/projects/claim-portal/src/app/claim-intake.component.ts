import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MsalService } from '@azure/msal-angular';

import { getRuntimeAuthConfiguration } from './auth.config';

interface IntakeCompleteEvent {
    sessionId: string;
    claimId: string;
    missingFields: { fieldName: string; isBlocking: boolean; message: string }[];
    collectedFields: Record<string, string>;
    isReadyForSubmission: boolean;
}

@Component({
    selector: 'app-claim-intake',
    imports: [CommonModule, FormsModule, RouterLink],
    template: `
    <main class="intake-shell">
      <section class="conversation" aria-label="Claim intake conversation">
        <header><p class="eyebrow">Claim intake</p><h1>Tell us what happened</h1></header>
        <div class="messages" aria-live="polite">
          @for (message of messages(); track $index) {
            <p [class.customer]="message.author === 'customer'" [class.assistant]="message.author === 'assistant'">{{ message.text }}</p>
          }
          @if (streamingText()) { <p class="assistant">{{ streamingText() }}</p> }
        </div>
        <form (ngSubmit)="sendMessage()">
          <label for="claim-message">Message</label>
          <textarea id="claim-message" [(ngModel)]="draft" name="message" rows="3" placeholder="For example: PolicyNumber: POL-123; ClaimType: auto"></textarea>
          <button type="submit" [disabled]="isStreaming() || !draft.trim()">Send</button>
        </form>
      </section>

      <aside class="documents">
        <h2>Supporting documents</h2>
        <div class="drop-zone" (dragover)="allowDrop($event)" (drop)="dropFiles($event)">
          <input #fileInput type="file" accept=".pdf,.jpg,.jpeg,.png,.tif,.tiff" multiple (change)="selectFiles($event)" hidden>
          <button type="button" (click)="fileInput.click()" [disabled]="!claimId() || uploading()">Choose files</button>
          <p>Drag PDF, JPEG, PNG, or TIFF files here.</p>
        </div>
        @if (!claimId()) { <p class="hint">Start the conversation to create an upload session.</p> }
        @if (claimId(); as id) { <a [routerLink]="['/claims', id]">Track this claim</a> }
        @for (file of uploadedFiles(); track file) { <p class="upload">{{ file }}</p> }
        @if (uploadError()) { <p class="error">{{ uploadError() }}</p> }
      </aside>
    </main>
  `,
    styles: [`
    .intake-shell { display: grid; gap: 2rem; grid-template-columns: minmax(0, 2fr) minmax(18rem, 1fr); max-width: 72rem; margin: 3rem auto; padding: 0 1rem; font-family: 'Segoe UI', sans-serif; }
    .conversation, .documents { border: 1px solid #d7dde2; border-radius: 8px; padding: 1.5rem; background: #fff; }
    .eyebrow { color: #0c6b63; font-weight: 700; text-transform: uppercase; font-size: .75rem; }
    h1, h2 { margin: .25rem 0 1rem; color: #183247; } .messages { min-height: 18rem; display: grid; align-content: start; gap: .75rem; }
    .messages p { max-width: 80%; padding: .75rem 1rem; border-radius: 6px; margin: 0; } .assistant { background: #eef4f2; color: #183247; } .customer { margin-left: auto !important; background: #183247; color: #fff; }
    textarea { box-sizing: border-box; width: 100%; margin: .5rem 0; padding: .75rem; } button { background: #0c6b63; color: #fff; border: 0; border-radius: 4px; padding: .65rem 1rem; font-weight: 600; } button:disabled { opacity: .55; }
    .drop-zone { border: 2px dashed #7a9b98; padding: 2rem 1rem; text-align: center; } .hint { color: #637381; } .upload { color: #0c6b63; overflow-wrap: anywhere; } .error { color: #a4262c; }
    @media (max-width: 720px) { .intake-shell { grid-template-columns: 1fr; margin-top: 1rem; } }
  `]
})
export class ClaimIntakeComponent {
    private readonly msal = inject(MsalService);
    protected readonly messages = signal<{ author: 'assistant' | 'customer'; text: string }[]>([
        { author: 'assistant', text: 'I can help collect your claim details. Start with your policy number and claim type.' }
    ]);
    protected readonly streamingText = signal('');
    protected readonly claimId = signal<string | undefined>(undefined);
    protected readonly uploadedFiles = signal<string[]>([]);
    protected readonly uploadError = signal('');
    protected readonly isStreaming = signal(false);
    protected readonly uploading = signal(false);
    protected draft = '';
    private sessionId: string | undefined;

    protected async sendMessage(): Promise<void> {
        const message = this.draft.trim();
        if (!message) return;

        this.messages.update((messages) => [...messages, { author: 'customer', text: message }]);
        this.draft = '';
        this.streamingText.set('');
        this.isStreaming.set(true);

        try {
            const response = await fetch(`${this.apiBaseUrl}/api/v1/claims/intake/conversation/stream`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${await this.accessToken()}` },
                body: JSON.stringify({ sessionId: this.sessionId, message })
            });
            if (!response.ok || !response.body) throw new Error('Unable to continue intake conversation.');
            await this.readStream(response.body);
        } catch (error) {
            this.messages.update((messages) => [...messages, { author: 'assistant', text: error instanceof Error ? error.message : 'Unable to continue intake conversation.' }]);
        } finally {
            this.isStreaming.set(false);
        }
    }

    protected allowDrop(event: DragEvent): void { event.preventDefault(); }
    protected dropFiles(event: DragEvent): void { event.preventDefault(); void this.upload(Array.from(event.dataTransfer?.files ?? [])); }
    protected selectFiles(event: Event): void { void this.upload(Array.from((event.target as HTMLInputElement).files ?? [])); }

    private async upload(files: File[]): Promise<void> {
        if (!this.claimId() || files.length === 0) return;
        this.uploadError.set(''); this.uploading.set(true);
        try {
            for (const file of files) {
                const formData = new FormData(); formData.append('file', file);
                const response = await fetch(`${this.apiBaseUrl}/api/v1/claims/${this.claimId()}/documents`, {
                    method: 'POST', headers: { Authorization: `Bearer ${await this.accessToken()}` }, body: formData
                });
                if (!response.ok) throw new Error((await response.json().catch(() => ({ error: 'Document upload failed.' }))).error);
                this.uploadedFiles.update((uploaded) => [...uploaded, file.name]);
            }
        } catch (error) { this.uploadError.set(error instanceof Error ? error.message : 'Document upload failed.'); }
        finally { this.uploading.set(false); }
    }

    private async readStream(body: ReadableStream<Uint8Array>): Promise<void> {
        const reader = body.getReader(); const decoder = new TextDecoder(); let buffer = '';
        while (true) {
            const { done, value } = await reader.read(); if (done) break;
            buffer += decoder.decode(value, { stream: true });
            const events = buffer.split('\n\n'); buffer = events.pop() ?? '';
            for (const event of events) this.consumeEvent(event);
        }
    }

    private consumeEvent(event: string): void {
        const eventName = event.match(/^event: (.+)$/m)?.[1];
        const payload = event.match(/^data: (.+)$/m)?.[1]; if (!eventName || !payload) return;
        const data = JSON.parse(payload) as { text?: string } | IntakeCompleteEvent;
        if (eventName === 'delta') this.streamingText.update((text) => text + (data as { text: string }).text);
        if (eventName === 'complete') {
            const complete = data as IntakeCompleteEvent; this.sessionId = complete.sessionId; this.claimId.set(complete.claimId);
            this.messages.update((messages) => [...messages, { author: 'assistant', text: this.streamingText() }]); this.streamingText.set('');
        }
    }

    private get apiBaseUrl(): string { return getRuntimeAuthConfiguration()?.apiBaseUrl?.replace(/\/$/, '') ?? ''; }
    private async accessToken(): Promise<string> {
        const account = this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0];
        if (!account) throw new Error('Sign in is required to start a claim.');
        const scope = getRuntimeAuthConfiguration()?.apiScope;
        return (await this.msal.instance.acquireTokenSilent({ account, scopes: scope ? [scope] : ['openid', 'profile'] })).accessToken;
    }
}