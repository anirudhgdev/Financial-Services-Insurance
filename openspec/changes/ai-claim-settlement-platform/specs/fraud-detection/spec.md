## Purpose

Assesses the fraud risk of each claim by computing a composite risk score, detecting duplicate submissions, and identifying suspicious behavioral and claim patterns — providing a structured fraud verdict with explainable signals to downstream decision agents.

## ADDED Requirements

### Requirement: Composite fraud risk scoring
The Fraud Detection Agent SHALL compute a fraud risk score between 0.0 and 1.0 for each claim by aggregating signals from: the external Fraud Detection Service, Azure OpenAI anomaly reasoning, claim history for the policy, and pattern-matching rules. The score SHALL be stored in the claim record alongside a list of contributing signals.

#### Scenario: Low fraud risk claim
- **WHEN** the computed fraud risk score is below 0.30
- **THEN** the agent SHALL record a `FRAUD_LOW` verdict and the claim SHALL proceed to the Settlement Decision Agent without friction

#### Scenario: Medium fraud risk claim
- **WHEN** the computed fraud risk score is between 0.30 and 0.69 (inclusive)
- **THEN** the agent SHALL record a `FRAUD_MEDIUM` verdict, include the top 3 contributing signals, and flag the claim for enhanced scrutiny in the Settlement Decision Agent

#### Scenario: High fraud risk claim
- **WHEN** the computed fraud risk score is 0.70 or above
- **THEN** the agent SHALL record a `FRAUD_HIGH` verdict with all contributing signals and the claim SHALL be automatically routed to human review

### Requirement: Duplicate claim detection
The agent SHALL query the claims database to detect duplicate submissions sharing the same policy number, date of loss, and loss type within a configurable deduplication window (default: 90 days). Detected duplicates SHALL be linked to the original claim record.

#### Scenario: Duplicate claim detected
- **WHEN** an incoming claim matches an existing claim on policy number, date of loss, and loss type within the deduplication window
- **THEN** the agent SHALL record a `DUPLICATE_CLAIM` flag, link to the original claim ID, set the fraud score contribution to +0.40, and route the claim to human review

#### Scenario: No duplicate found
- **WHEN** no existing claim matches within the deduplication window
- **THEN** the agent SHALL record `NO_DUPLICATE` and continue fraud scoring

### Requirement: Suspicious pattern detection
The agent SHALL evaluate claims against a configurable set of pattern rules including: claim frequency thresholds (e.g., more than 3 claims for the same policy in 12 months), loss amounts rounded to the nearest thousand, claims filed within 30 days of policy inception, and geographic anomalies. Each triggered rule SHALL be recorded as a named signal.

#### Scenario: High-frequency claimant
- **WHEN** a policy has more than 3 claims in the preceding 12 months
- **THEN** the agent SHALL add `HIGH_FREQUENCY_CLAIMANT` as a fraud signal and increase the risk score accordingly

#### Scenario: New policy claim
- **WHEN** a claim is filed within 30 days of policy inception
- **THEN** the agent SHALL add `NEW_POLICY_CLAIM` as a fraud signal

#### Scenario: Round-number loss amount
- **WHEN** the claimed loss amount is a round number (divisible by 1000) and exceeds 5,000 in the policy's base currency
- **THEN** the agent SHALL add `ROUND_AMOUNT` as a fraud signal

### Requirement: Fraud signal explainability
For every claim, the agent SHALL produce a human-readable explanation of the fraud verdict, listing each signal, its contribution weight, and the data point that triggered it. This explanation SHALL be stored in the claim record and surfaced to human reviewers.

#### Scenario: Fraud explanation generated
- **WHEN** fraud scoring completes for any claim
- **THEN** the agent SHALL store a structured explanation with at least: overall score, verdict, and each triggered signal with its weight and evidence

### Requirement: Fraud Detection Service resilience
The agent SHALL call the external Fraud Detection Service with retry-with-backoff (up to 3 attempts). If the service is unavailable, the agent SHALL fall back to rule-based scoring alone and record `FRAUD_SERVICE_UNAVAILABLE` in the claim metadata.

#### Scenario: Fraud Detection Service unavailable
- **WHEN** the Fraud Detection Service fails after 3 retry attempts
- **THEN** the agent SHALL complete scoring using pattern rules only, record `FRAUD_SERVICE_UNAVAILABLE`, and proceed with the degraded score flagged for review
