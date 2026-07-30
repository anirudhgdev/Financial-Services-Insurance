## Purpose

Aggregates the outputs of all upstream agents to produce a final claim decision recommendation (Approve, Reject, or Manual Review) with a confidence score, a recommended settlement amount, and a plain-language explainable reasoning narrative — providing the authoritative disposition signal for the claim lifecycle.

## ADDED Requirements

### Requirement: Multi-agent output aggregation
The Settlement Decision Agent SHALL receive and validate the structured outputs of the Claim Intake, Document Analysis, Policy Validation, and Fraud Detection agents before computing a decision. If any upstream agent's output is absent or in an error state, the agent SHALL route the claim to human review rather than produce an automated decision.

#### Scenario: All upstream outputs present
- **WHEN** all four upstream agent outputs are present and in a non-error state
- **THEN** the agent SHALL proceed to compute a decision recommendation

#### Scenario: Missing upstream output
- **WHEN** any upstream agent output is absent or in error state
- **THEN** the agent SHALL record `DECISION_BLOCKED_MISSING_INPUT`, list the missing outputs, and route the claim to the Human Review Agent

### Requirement: Decision recommendation
The agent SHALL produce one of three recommendations: `APPROVE`, `REJECT`, or `MANUAL_REVIEW`. The recommendation SHALL be computed using a weighted rule engine combined with Azure OpenAI reasoning. Decision rules SHALL be configurable per insurance provider.

#### Scenario: Auto-approval — low risk, valid policy, full coverage
- **WHEN** policy verdict is `POLICY_VALID`, coverage verdict is not `COVERAGE_EXCLUDED`, fraud verdict is `FRAUD_LOW`, and the requested amount is within coverage limits
- **THEN** the agent SHALL recommend `APPROVE`

#### Scenario: Auto-rejection — expired policy
- **WHEN** policy verdict is `POLICY_EXPIRED` or `POLICY_NOT_FOUND`
- **THEN** the agent SHALL recommend `REJECT` with the rejection reason `POLICY_INVALID`

#### Scenario: Auto-rejection — coverage excluded
- **WHEN** coverage verdict is `COVERAGE_EXCLUDED`
- **THEN** the agent SHALL recommend `REJECT` with the rejection reason `COVERAGE_EXCLUDED` and the applicable exclusion clause reference

#### Scenario: Manual review — high fraud risk
- **WHEN** fraud verdict is `FRAUD_HIGH` or `FRAUD_MEDIUM` and claim amount exceeds the provider's manual-review threshold
- **THEN** the agent SHALL recommend `MANUAL_REVIEW` with the fraud signals listed

#### Scenario: Manual review — ambiguous policy check
- **WHEN** policy verdict is `POLICY_CHECK_UNAVAILABLE`
- **THEN** the agent SHALL recommend `MANUAL_REVIEW`

### Requirement: Recommended settlement amount
For claims recommended for `APPROVE` or `MANUAL_REVIEW`, the agent SHALL compute a recommended settlement amount as: min(claimed amount, coverage limit) minus the applicable deductible. The computation SHALL be stored in the claim record with its inputs.

#### Scenario: Settlement amount computed for approved claim
- **WHEN** the recommendation is `APPROVE`
- **THEN** the agent SHALL store the recommended settlement amount, coverage limit applied, deductible amount, and final payable amount in the claim record

### Requirement: Confidence score
The agent SHALL produce a confidence score between 0.0 and 1.0 for every recommendation. The score SHALL reflect the completeness and quality of upstream inputs. Recommendations with confidence below 0.70 SHALL be automatically escalated to `MANUAL_REVIEW` regardless of the computed recommendation.

#### Scenario: Low-confidence auto-approval escalated
- **WHEN** the computed recommendation is `APPROVE` but confidence score is below 0.70
- **THEN** the agent SHALL override the recommendation to `MANUAL_REVIEW` and record the low-confidence reason

### Requirement: Explainable reasoning narrative
The agent SHALL generate a plain-language reasoning narrative (150–500 words) using Azure OpenAI that explains the recommendation in terms of policy facts, coverage findings, fraud signals, and document evidence. The narrative SHALL be stored in the claim record and presented to human reviewers and customers.

#### Scenario: Reasoning narrative generated for all decisions
- **WHEN** a recommendation is produced
- **THEN** the agent SHALL generate and store a 150–500 word reasoning narrative referencing the key evidence from upstream agents

### Requirement: Decision immutability
Once a decision recommendation is recorded in the claim record, it SHALL be immutable. Any subsequent reconsideration SHALL create a new decision record version with a reference to the superseded record.

#### Scenario: Decision record immutability
- **WHEN** a decision record is persisted
- **THEN** the system SHALL prevent modification of the original record and require a new versioned record for any override
