import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MsalService } from '@azure/msal-angular';

import { ClaimStatusTrackerComponent } from './claim-status-tracker.component';

describe('ClaimStatusTrackerComponent', () => {
    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [ClaimStatusTrackerComponent],
            providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: MsalService, useValue: {} }]
        }).compileComponents();
    });

    it('formats complete and long-running claim estimates', () => {
        const component = TestBed.createComponent(ClaimStatusTrackerComponent).componentInstance;

        expect((component as any).remainingTime(0)).toBe('Complete');
        expect((component as any).remainingTime(2880)).toBe('2 business day(s)');
    });
});