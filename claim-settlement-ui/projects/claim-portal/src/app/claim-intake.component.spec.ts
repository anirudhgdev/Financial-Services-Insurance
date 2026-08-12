import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MsalService } from '@azure/msal-angular';

import { ClaimIntakeComponent } from './claim-intake.component';

describe('ClaimIntakeComponent', () => {
    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [ClaimIntakeComponent],
            providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: MsalService, useValue: {} }]
        }).compileComponents();
    });

    it('records the claim ID when the streamed conversation completes', () => {
        const component = TestBed.createComponent(ClaimIntakeComponent).componentInstance;
        (component as any).streamingText.set('Please upload your documents.');

        (component as any).consumeEvent('event: complete\ndata: {"sessionId":"session-1","claimId":"claim-1","missingFields":[],"collectedFields":{},"isReadyForSubmission":true}');

        expect((component as any).claimId()).toBe('claim-1');
        expect((component as any).messages().at(-1).text).toBe('Please upload your documents.');
    });
});