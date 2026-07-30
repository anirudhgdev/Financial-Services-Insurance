## Purpose

Delivers timely, multi-channel (email and SMS) notifications to customers and internal stakeholders at every key claim lifecycle event, and requests additional information from customers when required by upstream agents.

## ADDED Requirements

### Requirement: Claim lifecycle notifications to customers
The Notification Agent SHALL send notifications to the customer at the following lifecycle events: claim submission confirmation, document processing complete, policy validation complete, fraud check complete, settlement decision issued, human review assigned, adjuster decision issued, and settlement payment initiated. Each notification SHALL include the claim ID, event type, a plain-language message, and a link to the claim status portal.

#### Scenario: Claim submission confirmation
- **WHEN** a claim record is created with status `INTAKE_COMPLETE`
- **THEN** the system SHALL send an email (and SMS if opted in) to the customer within 60 seconds containing the claim ID and a summary of next steps

#### Scenario: Settlement decision notification
- **WHEN** the Settlement Decision Agent records an `APPROVE` or `REJECT` recommendation
- **THEN** the system SHALL notify the customer within 60 seconds with the decision, reasoning summary, and — for approvals — the recommended settlement amount

#### Scenario: Human review assignment notification
- **WHEN** a claim is assigned to a human adjuster
- **THEN** the system SHALL notify the customer that the claim is under specialist review and provide an estimated response timeframe based on provider SLA configuration

### Requirement: Additional information requests
When the Document Analysis Agent or Claim Intake Agent detects missing blocking information, the Notification Agent SHALL send a structured request to the customer listing the specific missing items, with a deadline for response (default: 7 days, configurable per provider).

#### Scenario: Missing document request
- **WHEN** the Document Analysis Agent produces a gap report with one or more blocking missing fields
- **THEN** the system SHALL send a notification to the customer listing each missing item with instructions for submission and a response deadline

#### Scenario: Response deadline passed
- **WHEN** the customer does not respond to an information request within the configured deadline
- **THEN** the system SHALL send a reminder notification 24 hours before the deadline, and if still unresolved after the deadline, escalate the claim to manual review with status `INFO_TIMEOUT`

### Requirement: Internal stakeholder notifications
The agent SHALL notify adjusters when a new claim is assigned to them, notify supervisors on SLA breaches, and notify system administrators on service errors that require intervention. Internal notifications SHALL be delivered via email.

#### Scenario: Adjuster assignment notification
- **WHEN** a claim is assigned to an adjuster
- **THEN** the system SHALL send an email to the adjuster within 30 seconds containing the claim ID, claim type, priority level, and a direct link to the review portal

#### Scenario: Supervisor SLA breach alert
- **WHEN** a claim's human-review SLA is breached
- **THEN** the system SHALL send an email alert to the configured supervisor with the claim ID, assigned adjuster, and hours elapsed

### Requirement: Notification delivery reliability
All notifications SHALL be delivered via the external Notification Service. The agent SHALL implement at-least-once delivery with idempotent message IDs to prevent duplicate sends. Failed deliveries SHALL be retried up to 3 times with exponential backoff. Undeliverable notifications SHALL be logged and surfaced in the observability dashboard.

#### Scenario: Notification service temporarily unavailable
- **WHEN** the Notification Service returns an error
- **THEN** the agent SHALL retry up to 3 times with exponential backoff and, if all retries fail, log the failure and queue the notification for manual review

#### Scenario: Duplicate notification prevention
- **WHEN** the same lifecycle event triggers more than one notification attempt for the same claim and event type
- **THEN** the system SHALL deduplicate using the idempotent message ID and send only one notification

### Requirement: Customer communication preferences
The system SHALL respect customer channel preferences (email only, SMS only, or both) as configured in the customer profile. Customers SHALL be able to update their preferences at any time through the claim status portal.

#### Scenario: SMS opt-out honored
- **WHEN** a customer has opted out of SMS notifications
- **THEN** the system SHALL send notifications via email only and SHALL NOT send any SMS messages to that customer
