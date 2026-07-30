## Purpose

Provides an automated evaluation framework that validates the end-to-end multi-agent claim settlement workflow against a defined test dataset, measuring decision accuracy, fraud detection performance, agent latency, hallucination rates, and cost-per-claim — producing structured benchmark reports for continuous quality assurance.

## ADDED Requirements

### Requirement: Test dataset definition
The harness SHALL maintain a versioned test dataset containing at minimum the following scenario types with ground-truth expected decisions: valid claim (auto-approve), expired policy (reject), duplicate claim (manual review), missing documents (information request), high fraud score (manual review), large claim amount exceeding manual-review threshold (manual review), and multiple damaged assets (auto-approve or manual review depending on amount). Each test case SHALL include: scenario ID, scenario type, claim input payload, supporting document fixtures, expected pipeline decision, expected fraud verdict, expected policy verdict, and expected notification events.

#### Scenario: Test dataset loaded
- **WHEN** the evaluation harness is invoked
- **THEN** all test cases in the dataset SHALL be loaded and validated for schema completeness before execution begins

#### Scenario: Incomplete test case
- **WHEN** a test case is missing a required field (expected decision, input payload, or scenario type)
- **THEN** the harness SHALL reject that test case, log the missing field, and continue with the remaining cases

### Requirement: Automated end-to-end evaluation
The harness SHALL submit each test case through the live claim pipeline (or a designated evaluation environment) and compare the actual pipeline outputs against ground-truth expectations. Evaluation SHALL be triggerable via a CLI command and via a CI/CD pipeline hook.

#### Scenario: Evaluation run completes
- **WHEN** the harness completes processing all test cases
- **THEN** it SHALL produce a structured evaluation report with per-case pass/fail results and aggregate metrics

#### Scenario: Pipeline timeout during evaluation
- **WHEN** a test case does not complete within the configured evaluation timeout (default: 120 seconds per claim)
- **THEN** the harness SHALL mark the case as `TIMEOUT`, record the elapsed time, and continue with remaining cases

### Requirement: Decision accuracy metrics
The harness SHALL compute and report: overall decision accuracy (percentage of correct recommendations vs. ground truth), precision and recall for `APPROVE`, `REJECT`, and `MANUAL_REVIEW` classes, and F1 score per class.

#### Scenario: Accuracy metrics computed
- **WHEN** evaluation completes
- **THEN** the report SHALL include overall accuracy, per-class precision, recall, and F1 score computed against ground truth

### Requirement: Fraud detection metrics
The harness SHALL compute fraud detection performance metrics including: fraud detection rate (true positive rate among known-fraud cases), false positive rate (genuine claims incorrectly flagged), AUC-ROC for fraud scores, and mean fraud score per scenario type.

#### Scenario: Fraud metrics included in report
- **WHEN** evaluation completes
- **THEN** the report SHALL include fraud detection rate, false positive rate, AUC-ROC, and mean fraud score broken down by scenario type

### Requirement: Latency measurement
The harness SHALL record wall-clock latency for each pipeline stage (intake-to-document-analysis, document-analysis-to-policy-validation, policy-validation-to-fraud-detection, fraud-detection-to-settlement-decision, total end-to-end) per test case. The report SHALL include P50, P95, and P99 latency percentiles per stage.

#### Scenario: Latency percentiles reported
- **WHEN** evaluation completes
- **THEN** the report SHALL include P50, P95, and P99 latency for each pipeline stage and for total end-to-end duration

### Requirement: Hallucination detection
The harness SHALL evaluate agent-generated text outputs (reasoning narratives, review summaries) for factual consistency with the claim record by checking that all claim IDs, policy numbers, dates, and monetary amounts cited in generated text are present and accurate in the structured claim data. Inconsistencies SHALL be flagged as hallucinations.

#### Scenario: Hallucination detected in reasoning narrative
- **WHEN** a reasoning narrative contains a monetary amount, date, or identifier not present in the claim record
- **THEN** the harness SHALL flag the case as `HALLUCINATION_DETECTED`, record the inconsistent value, and include it in the report

#### Scenario: No hallucinations detected
- **WHEN** all facts in generated text are consistent with the claim record
- **THEN** the case SHALL be marked `HALLUCINATION_NONE`

### Requirement: Tool invocation validation
The harness SHALL verify that each agent invoked the expected set of external tools (Policy Management API, Fraud Detection Service, Document Intelligence, Notification Service) and that no unexpected tool invocations occurred. The report SHALL list any missing or unexpected tool calls per agent per test case.

#### Scenario: Expected tool calls validated
- **WHEN** evaluation completes for a test case
- **THEN** the report SHALL list each agent's actual tool invocations vs. expected, flagging discrepancies as `TOOL_MISSING` or `TOOL_UNEXPECTED`

### Requirement: Human-review rate measurement
The harness SHALL compute the human-review rate as the percentage of test cases resulting in `MANUAL_REVIEW`, broken down by scenario type. The report SHALL flag scenario types where the human-review rate diverges from the expected rate by more than 10 percentage points.

#### Scenario: Human-review rate computed per scenario type
- **WHEN** evaluation completes
- **THEN** the report SHALL include human-review rate per scenario type and flag anomalies exceeding the 10-point threshold

### Requirement: Cost-per-claim estimation
The harness SHALL estimate the LLM token cost per claim processed by recording input and output token counts for each Azure AI Foundry call. The report SHALL include mean, P95, and total estimated cost (USD) per evaluation run, broken down by agent.

#### Scenario: Cost estimate included in report
- **WHEN** evaluation completes
- **THEN** the report SHALL include per-agent and total token usage with estimated USD cost based on current Azure AI Foundry pricing

### Requirement: Benchmark report output
The harness SHALL produce a benchmark report in both JSON (machine-readable) and Markdown (human-readable) formats. The report SHALL include: run timestamp, environment identifier, dataset version, total cases, pass/fail counts, all computed metrics, and a per-case detail table. Reports SHALL be stored in Azure Blob Storage and accessible via a CLI command.

#### Scenario: Report generated in JSON and Markdown
- **WHEN** evaluation completes
- **THEN** the harness SHALL write both a `.json` and a `.md` report file to the configured Azure Blob Storage container and print the report summary to stdout

#### Scenario: Report retrieval
- **WHEN** a user runs the report retrieval CLI command with a run ID
- **THEN** the harness SHALL download and display the Markdown report for that run
