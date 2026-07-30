## Purpose

Routes high-risk or ambiguous claims to licensed human adjusters, provides AI-generated review summaries to support efficient adjuster decision-making, captures the adjuster's final verdict, and returns the decision to the settlement pipeline.

## ADDED Requirements

### Requirement: Claim routing to human adjusters
The Human Review Agent SHALL receive claims with a `MANUAL_REVIEW` recommendation from the Settlement Decision Agent and assign them to an available adjuster via the adjuster assignment queue. Assignment SHALL consider adjuster workload, claim type specialization, and provider-configured routing rules.

#### Scenario: Claim routed to adjuster queue
- **WHEN** the Settlement Decision Agent produces a `MANUAL_REVIEW` recommendation
- **THEN** the system SHALL place the claim in the adjuster assignment queue within 30 seconds and notify the assigned adjuster via the Notification Agent

#### Scenario: No adjuster available
- **WHEN** no adjuster is available in the assignment queue
- **THEN** the system SHALL place the claim in a pending queue, notify the customer of the delay, and retry assignment every 15 minutes

### Requirement: AI-assisted adjuster review summary
The agent SHALL generate a structured review package for each assigned claim containing: claim summary, policy validation verdict, fraud signals and score, document extraction highlights, settlement decision reasoning narrative, and recommended settlement amount. The package SHALL be presented in the adjuster portal.

#### Scenario: Review package presented to adjuster
- **WHEN** an adjuster opens an assigned claim in the review portal
- **THEN** the system SHALL display the full AI-generated review package within 3 seconds

#### Scenario: Review package completeness
- **WHEN** any upstream agent output is missing from the review package
- **THEN** the system SHALL display a warning indicating the missing section and allow the adjuster to proceed or request re-analysis

### Requirement: Adjuster decision capture
The system SHALL allow the adjuster to record a final decision of `APPROVE`, `REJECT`, or `ESCALATE`, enter a free-text decision rationale (minimum 20 characters), and optionally override the recommended settlement amount. All adjuster inputs SHALL be persisted in the claim record with the adjuster's identity and timestamp.

#### Scenario: Adjuster approves claim
- **WHEN** an adjuster selects `APPROVE` and submits a rationale
- **THEN** the system SHALL record the decision, adjuster ID, timestamp, final settlement amount, and rationale in the claim record and trigger the Notification Agent to inform the customer

#### Scenario: Adjuster rejects claim
- **WHEN** an adjuster selects `REJECT` and submits a rationale
- **THEN** the system SHALL record the rejection decision with reason and trigger the customer notification flow

#### Scenario: Rationale too short
- **WHEN** an adjuster attempts to submit a decision with a rationale shorter than 20 characters
- **THEN** the system SHALL reject the submission and display a validation error

### Requirement: Adjuster SLA tracking
The system SHALL track time-to-decision for each claim in the human review queue. If a claim remains unactioned for more than the provider-configured SLA period (default: 48 hours), the system SHALL escalate to a supervisor and notify the customer of the delay.

#### Scenario: SLA breach detected
- **WHEN** a claim in the human review queue exceeds the configured SLA period without an adjuster decision
- **THEN** the system SHALL escalate the claim to a supervisor, send a supervisor notification, and update the claim status to `SLA_BREACHED`

### Requirement: Decision audit trail
Every adjuster action on a claim (view, decision, override) SHALL be recorded in the audit log with adjuster identity, timestamp, action type, and relevant data. The audit trail SHALL be immutable and exportable.

#### Scenario: Adjuster action logged
- **WHEN** an adjuster views or acts on a claim
- **THEN** the system SHALL write an immutable audit log entry within 1 second of the action
