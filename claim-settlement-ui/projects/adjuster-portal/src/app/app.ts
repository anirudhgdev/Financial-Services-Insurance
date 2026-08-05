import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface QueueClaim {
  claimId: string;
  claimant: string;
  claimType: string;
  status: 'MANUAL_REVIEW_ASSIGNED' | 'SLA_BREACHED' | 'PENDING_ASSIGNMENT';
  priority: 'High' | 'Medium' | 'Low';
  recommendedAmount: number;
  assignedAdjuster: string;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('Adjuster Workbench');

  protected readonly queue = signal<QueueClaim[]>([
    {
      claimId: 'CLM-2026-00123',
      claimant: 'Alex Johnson',
      claimType: 'auto',
      status: 'MANUAL_REVIEW_ASSIGNED',
      priority: 'High',
      recommendedAmount: 1250,
      assignedAdjuster: 'adjuster-auto-1'
    },
    {
      claimId: 'CLM-2026-00124',
      claimant: 'Priya Kumar',
      claimType: 'property',
      status: 'SLA_BREACHED',
      priority: 'High',
      recommendedAmount: 4800,
      assignedAdjuster: 'adjuster-property-1'
    },
    {
      claimId: 'CLM-2026-00125',
      claimant: 'Morgan Lee',
      claimType: 'health',
      status: 'PENDING_ASSIGNMENT',
      priority: 'Medium',
      recommendedAmount: 900,
      assignedAdjuster: 'pending'
    }
  ]);

  protected readonly selectedClaimId = signal(this.queue()[0].claimId);
  protected readonly selectedClaim = computed(() =>
    this.queue().find((claim) => claim.claimId === this.selectedClaimId()) ?? this.queue()[0]
  );

  protected decision: 'APPROVE' | 'REJECT' | 'ESCALATE' = 'APPROVE';
  protected rationale = '';
  protected settlementOverride: number | null = null;
  protected message = '';

  protected selectClaim(claimId: string): void {
    this.selectedClaimId.set(claimId);
    this.message = '';
  }

  protected submitDecision(): void {
    if (this.rationale.trim().length < 20) {
      this.message = 'Rationale must be at least 20 characters.';
      return;
    }

    this.message = `Decision ${this.decision} submitted for ${this.selectedClaim().claimId}.`;
    this.rationale = '';
    this.settlementOverride = null;
  }
}
