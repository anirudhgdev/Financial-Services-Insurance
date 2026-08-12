import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MsalService } from '@azure/msal-angular';

import { AdjusterWorkbenchComponent } from './adjuster-workbench.component';

describe('AdjusterWorkbenchComponent', () => {
    beforeEach(async () => {
        spyOn(window, 'fetch').and.resolveTo(new Response(JSON.stringify({ claims: [], totalCount: 0 })));
        await TestBed.configureTestingModule({
            imports: [AdjusterWorkbenchComponent],
            providers: [provideZonelessChangeDetection(), { provide: MsalService, useValue: {} }]
        }).compileComponents();
    });

    it('rejects a decision rationale shorter than 20 characters before submission', async () => {
        const component = TestBed.createComponent(AdjusterWorkbenchComponent).componentInstance;
        (component as any).rationale = 'too short';

        await (component as any).submitDecision();

        expect((component as any).message).toBe('Rationale must be at least 20 characters.');
    });
});