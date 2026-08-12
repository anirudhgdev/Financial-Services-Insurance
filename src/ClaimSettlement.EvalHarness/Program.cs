using Azure.Core;
using Azure.Identity;
using ClaimSettlement.EvalHarness;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

if (args.Length == 0 || !string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
{
	Console.Error.WriteLine("Usage: dotnet run -- run --env <environment> --dataset <version>");
	return 1;
}

var options = ParseOptions(args.Skip(1));
if (!options.TryGetValue("env", out var environment) || string.IsNullOrWhiteSpace(environment) ||
	!options.TryGetValue("dataset", out var datasetVersion) || string.IsNullOrWhiteSpace(datasetVersion))
{
	Console.Error.WriteLine("Both --env and --dataset are required.");
	return 1;
}

var datasetPath = Path.Combine(AppContext.BaseDirectory, "Datasets", $"{datasetVersion}.json");
if (!File.Exists(datasetPath))
{
	Console.Error.WriteLine($"Dataset '{datasetVersion}' was not found at {datasetPath}.");
	return 1;
}

await using var stream = File.OpenRead(datasetPath);
var dataset = await JsonSerializer.DeserializeAsync<EvaluationDataset>(stream, JsonOptions.Default);
if (dataset is null || dataset.Cases.Count == 0)
{
	Console.Error.WriteLine($"Dataset '{datasetVersion}' does not contain any test cases.");
	return 1;
}

var invalidCases = dataset.Cases.Where(testCase => !testCase.IsValid()).ToList();
if (invalidCases.Count > 0)
{
	Console.Error.WriteLine($"Dataset contains incomplete cases: {string.Join(", ", invalidCases.Select(testCase => testCase.ScenarioId))}.");
	return 1;
}

var harnessConfiguration = HarnessConfiguration.FromEnvironment(environment);
var tokenPricing = AzureOpenAiTokenPricingConfiguration.FromEnvironment();
TokenCredential credential = harnessConfiguration.UseManagedIdentity
	? new ManagedIdentityCredential(harnessConfiguration.ManagedIdentityClientId)
	: new AzureCliCredential();
var accessToken = await credential.GetTokenAsync(
	new TokenRequestContext([harnessConfiguration.ApiScope]),
	CancellationToken.None);

using var httpClient = new HttpClient { BaseAddress = harnessConfiguration.ApiBaseUri };
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

var evaluationRunId = await CreateEvaluationRunAsync(httpClient, dataset.Version);
var outcomes = new List<EvaluationSubmissionOutcome>();
foreach (var testCase in dataset.Cases)
{
	outcomes.Add(await SubmitAndPollAsync(httpClient, evaluationRunId, testCase));
}

var accuracy = DecisionAccuracyMetrics.Compute(outcomes.Select(outcome => new DecisionEvaluationResult(outcome.ExpectedDecision, outcome.ActualDecision)));
var fraudMetrics = FraudMetrics.Compute(outcomes.Select(outcome => new FraudEvaluationResult(
	outcome.ScenarioType,
	outcome.ExpectedFraudVerdict,
	outcome.ActualFraudVerdict,
	outcome.FraudRiskScore)));
var latencyMetrics = LatencyMetrics.Compute(outcomes.Where(outcome => outcome.Latency is not null).Select(outcome => outcome.Latency!));
var qualityGates = EvaluationQualityGates.FromEnvironment();
var totalEndToEndP95 = latencyMetrics.FirstOrDefault(metric => string.Equals(metric.StageName, "total-end-to-end", StringComparison.OrdinalIgnoreCase))?.P95;
var hallucinationCount = outcomes.Count(outcome => outcome.HallucinationCheck is { IsConsistent: false });
var toolDiscrepancies = outcomes.SelectMany(outcome => outcome.ToolDiscrepancies).ToList();
var humanReviewRates = HumanReviewMetrics.Compute(outcomes.Select(outcome =>
	new HumanReviewEvaluationResult(outcome.ScenarioType, outcome.ExpectedDecision, outcome.ActualDecision)));
var costMetrics = CostMetrics.Compute(
	outcomes.SelectMany(outcome => outcome.TokenUsage),
	tokenPricing,
	outcomes.Count(outcome => outcome.Completed));
var report = new BenchmarkReport(
	evaluationRunId,
	DateTime.UtcNow,
	environment,
	dataset.Version,
	outcomes,
	accuracy,
	fraudMetrics,
	latencyMetrics,
	hallucinationCount,
	toolDiscrepancies.Count,
	humanReviewRates,
	costMetrics);
var reportPublication = await new BenchmarkReportWriter(harnessConfiguration.ReportsContainerUri, credential).PublishAsync(report, CancellationToken.None);
Console.WriteLine($"Evaluation run completed. Environment: {environment}; dataset: {dataset.Version}; cases: {outcomes.Count}; completed: {outcomes.Count(outcome => outcome.Completed)}; authentication: {harnessConfiguration.AuthenticationMode}.");
Console.WriteLine($"JSON Lines report: {reportPublication.JsonLines.Uri} (SHA-256: {reportPublication.JsonLines.Sha256}).");
Console.WriteLine($"Markdown report: {reportPublication.Markdown.Uri} (SHA-256: {reportPublication.Markdown.Sha256}).");
Console.WriteLine($"Decision accuracy: {accuracy.OverallAccuracy:P2} ({accuracy.CaseCount} cases).");
foreach (var metrics in accuracy.PerClass)
{
	Console.WriteLine($"{metrics.DecisionClass}: precision {metrics.Precision:P2}; recall {metrics.Recall:P2}; F1 {metrics.F1:P2}.");
}
Console.WriteLine($"Fraud detection rate: {fraudMetrics.DetectionRate:P2}; false positive rate: {fraudMetrics.FalsePositiveRate:P2}; AUC-ROC: {fraudMetrics.AucRoc?.ToString("F3") ?? "unavailable"}.");
foreach (var scenarioScore in fraudMetrics.MeanScoreByScenarioType)
{
	Console.WriteLine($"{scenarioScore.ScenarioType}: mean fraud score {scenarioScore.MeanRiskScore:F3}.");
}
foreach (var latency in latencyMetrics)
{
	Console.WriteLine($"{latency.StageName}: P50 {latency.P50.TotalSeconds:F2}s; P95 {latency.P95.TotalSeconds:F2}s; P99 {latency.P99.TotalSeconds:F2}s.");
}
Console.WriteLine($"Quality gates: accuracy >= {qualityGates.MinimumAccuracy:P2}; total end-to-end P95 <= {qualityGates.MaximumP95Latency.TotalSeconds:F2}s.");
Console.WriteLine($"Hallucination checks: {outcomes.Count(outcome => outcome.HallucinationCheck is not null) - hallucinationCount} consistent; {hallucinationCount} detected.");
Console.WriteLine($"Tool invocation checks: {outcomes.Count} cases; {toolDiscrepancies.Count} discrepancies.");
foreach (var discrepancy in toolDiscrepancies)
{
	Console.Error.WriteLine($"{discrepancy.Kind}: {discrepancy.AgentId} -> {discrepancy.ToolName}.");
}
foreach (var rate in humanReviewRates)
{
	Console.WriteLine($"{rate.ScenarioType}: expected human review {rate.ExpectedRate:P2}; actual {rate.ActualRate:P2}; anomaly {(rate.IsAnomalous ? "yes" : "no")}.");
}
foreach (var agentCost in costMetrics.PerAgent)
{
	Console.WriteLine($"{agentCost.AgentId}: input tokens {agentCost.InputTokenCount}; output tokens {agentCost.OutputTokenCount}; cost ${agentCost.CostUsd:F6}.");
}
Console.WriteLine($"Azure OpenAI cost: total ${costMetrics.TotalCostUsd:F6}; per completed claim ${costMetrics.CostPerCompletedClaimUsd?.ToString("F6") ?? "unavailable"}.");
foreach (var outcome in outcomes.Where(outcome => outcome.HallucinationCheck is { IsConsistent: false }))
{
	Console.Error.WriteLine($"{outcome.ScenarioId}: HALLUCINATION_DETECTED ({string.Join(", ", outcome.HallucinationCheck!.Inconsistencies)}).");
}
foreach (var outcome in outcomes.Where(outcome => !outcome.Completed))
{
	Console.Error.WriteLine($"{outcome.ScenarioId}: {outcome.Message}");
}

var meetsAccuracyGate = accuracy.OverallAccuracy >= qualityGates.MinimumAccuracy;
var meetsLatencyGate = totalEndToEndP95.HasValue && totalEndToEndP95.Value <= qualityGates.MaximumP95Latency;
if (!meetsAccuracyGate)
{
	Console.Error.WriteLine($"Accuracy gate failed: {accuracy.OverallAccuracy:P2} is below {qualityGates.MinimumAccuracy:P2}.");
}

if (!meetsLatencyGate)
{
	Console.Error.WriteLine(totalEndToEndP95.HasValue
		? $"Latency gate failed: total end-to-end P95 {totalEndToEndP95.Value.TotalSeconds:F2}s exceeds {qualityGates.MaximumP95Latency.TotalSeconds:F2}s."
		: "Latency gate failed: total end-to-end P95 was unavailable.");
}

return outcomes.All(outcome => outcome.Completed) && meetsAccuracyGate && meetsLatencyGate ? 0 : 1;

static async Task<Guid> CreateEvaluationRunAsync(HttpClient httpClient, string datasetVersion)
{
	using var response = await httpClient.PostAsJsonAsync("api/v1/evaluation-runs", new { datasetVersion });
	response.EnsureSuccessStatusCode();
	var run = await response.Content.ReadFromJsonAsync<EvaluationRunResponse>(JsonOptions.Default);
	return run?.RunId ?? throw new InvalidOperationException("Evaluation run creation did not return a run ID.");
}

static async Task<EvaluationSubmissionOutcome> SubmitAndPollAsync(HttpClient httpClient, Guid evaluationRunId, EvaluationCase testCase)
{
	var submittedAtUtc = DateTime.UtcNow;
	var fields = testCase.ClaimInput.EnumerateObject().ToDictionary(
		property => ToIntakeFieldName(property.Name),
		property => property.Value.ToString(),
		StringComparer.OrdinalIgnoreCase);
	fields.TryAdd("LossDate", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("O"));
	fields.TryAdd("LossDescription", $"Evaluation scenario: {testCase.ScenarioType}");

	using var conversationResponse = await httpClient.PostAsJsonAsync("api/v1/claims/intake/conversation", new
	{
		message = $"Submit evaluation scenario {testCase.ScenarioId}.",
		evaluationRunId,
		fields
	});
	if (!conversationResponse.IsSuccessStatusCode)
	{
		return EvaluationSubmissionOutcome.Failed(testCase, $"Intake returned {(int)conversationResponse.StatusCode}.");
	}

	var conversation = await conversationResponse.Content.ReadFromJsonAsync<IntakeConversationResponse>(JsonOptions.Default);
	if (conversation is null || string.IsNullOrWhiteSpace(conversation.SessionId))
	{
		return EvaluationSubmissionOutcome.Failed(testCase, "Intake response did not contain a session ID.");
	}

	using var completionResponse = await httpClient.PostAsJsonAsync("api/v1/claims/intake/complete", new { sessionId = conversation.SessionId });
	if (!completionResponse.IsSuccessStatusCode)
	{
		return EvaluationSubmissionOutcome.Failed(testCase, $"Completion returned {(int)completionResponse.StatusCode}.");
	}

	var completion = await completionResponse.Content.ReadFromJsonAsync<IntakeCompletionResponse>(JsonOptions.Default);
	if (completion?.ClaimId is not Guid claimId)
	{
		return EvaluationSubmissionOutcome.Failed(testCase, "Completion response did not contain a claim ID.");
	}

	var deadline = DateTime.UtcNow.AddMinutes(5);
	while (DateTime.UtcNow < deadline)
	{
		using var statusResponse = await httpClient.GetAsync($"api/v1/claims/{claimId}/status");
		if (statusResponse.IsSuccessStatusCode)
		{
			var status = await statusResponse.Content.ReadFromJsonAsync<ClaimStatusResponse>(JsonOptions.Default);
			if (status is not null && IsTerminalStatus(status.Status))
			{
				var results = await GetEvaluationResultsAsync(httpClient, evaluationRunId, claimId);
				if (results is null)
				{
					return EvaluationSubmissionOutcome.Failed(testCase, "Evaluation result output was unavailable.");
				}

				var fraud = ExtractFraudResult(results);
				var hallucinationCheck = HallucinationDetection.Check(
					testCase,
					claimId,
					ExtractNarratives(results),
					ExtractStructuredAmounts(results));
				var toolDiscrepancies = ToolInvocationValidation.Validate(results.ToolInvocations.Select(invocation =>
					new EvaluationToolInvocation(invocation.AgentId, invocation.ToolName, invocation.Outcome)));
				return EvaluationSubmissionOutcome.Succeeded(
					testCase,
					claimId,
					NormalizeDecision(status.Status),
					status.Status,
					fraud.verdict,
					fraud.riskScore,
					BuildPipelineLatency(submittedAtUtc, results),
					hallucinationCheck,
					toolDiscrepancies,
					ExtractTokenUsage(results));
			}
		}

		await Task.Delay(TimeSpan.FromSeconds(2));
	}

	return EvaluationSubmissionOutcome.Failed(testCase, "Pipeline did not reach a terminal status within five minutes.");
}

static IReadOnlyList<string> ExtractNarratives(EvaluationClaimResultsResponse results) => results.AgentOutputs
	.Where(output => output.AgentId is "SettlementDecisionAgent" or "HumanReviewAgent")
	.SelectMany(output => GetJsonStrings(output.Output, "Reasoning", "SettlementReasoning", "ClaimSummary", "PolicyValidationSummary", "FraudSummary"))
	.ToList();

static IReadOnlyList<decimal> ExtractStructuredAmounts(EvaluationClaimResultsResponse results) => results.AgentOutputs
	.Where(output => output.AgentId is "SettlementDecisionAgent" or "HumanReviewAgent")
	.SelectMany(output => GetJsonDecimals(output.Output, "RecommendedSettlementAmount", "AppliedCoverageLimit", "AppliedDeductible"))
	.ToList();

static IEnumerable<string> GetJsonStrings(JsonElement element, params string[] propertyNames)
{
	if (element.ValueKind == JsonValueKind.Object)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (property.Value.ValueKind == JsonValueKind.String && propertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
			{
				yield return property.Value.GetString() ?? string.Empty;
			}
			else
			{
				foreach (var value in GetJsonStrings(property.Value, propertyNames)) yield return value;
			}
		}
	}
	else if (element.ValueKind == JsonValueKind.Array)
	{
		foreach (var item in element.EnumerateArray())
		{
			foreach (var value in GetJsonStrings(item, propertyNames)) yield return value;
		}
	}
}

static IEnumerable<decimal> GetJsonDecimals(JsonElement element, params string[] propertyNames)
{
	if (element.ValueKind == JsonValueKind.Object)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (propertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.TryGetDecimal(out var value))
			{
				yield return value;
			}
			else
			{
				foreach (var nestedValue in GetJsonDecimals(property.Value, propertyNames)) yield return nestedValue;
			}
		}
	}
	else if (element.ValueKind == JsonValueKind.Array)
	{
		foreach (var item in element.EnumerateArray())
		{
			foreach (var value in GetJsonDecimals(item, propertyNames)) yield return value;
		}
	}
}

static PipelineLatency BuildPipelineLatency(DateTime submittedAtUtc, EvaluationClaimResultsResponse results)
{
	var timestamps = results.AgentOutputs
		.GroupBy(output => output.AgentId, StringComparer.OrdinalIgnoreCase)
		.ToDictionary(group => group.Key, group => group.Max(output => output.CreatedAtUtc), StringComparer.OrdinalIgnoreCase);
	var stages = new List<PipelineStageLatency>();

	AddStage("intake-to-document-analysis", submittedAtUtc, "DocumentAnalysisAgent");
	AddAgentStage("document-analysis-to-policy-validation", "DocumentAnalysisAgent", "PolicyValidationAgent");
	AddAgentStage("policy-validation-to-fraud-detection", "PolicyValidationAgent", "FraudDetectionAgent");
	AddAgentStage("fraud-detection-to-settlement-decision", "FraudDetectionAgent", "SettlementDecisionAgent");
	var lastOutputAtUtc = results.AgentOutputs.Count == 0 ? (DateTime?)null : results.AgentOutputs.Max(output => output.CreatedAtUtc);
	if (lastOutputAtUtc.HasValue && lastOutputAtUtc.Value >= submittedAtUtc)
	{
		stages.Add(new PipelineStageLatency("total-end-to-end", lastOutputAtUtc.Value - submittedAtUtc));
	}

	return new PipelineLatency(stages);

	void AddStage(string stageName, DateTime startUtc, string endAgentId)
	{
		if (timestamps.TryGetValue(endAgentId, out var endUtc) && endUtc >= startUtc)
		{
			stages.Add(new PipelineStageLatency(stageName, endUtc - startUtc));
		}
	}

	void AddAgentStage(string stageName, string startAgentId, string endAgentId)
	{
		if (timestamps.TryGetValue(startAgentId, out var startUtc) &&
			timestamps.TryGetValue(endAgentId, out var endUtc) &&
			endUtc >= startUtc)
		{
			stages.Add(new PipelineStageLatency(stageName, endUtc - startUtc));
		}
	}
}

static async Task<EvaluationClaimResultsResponse?> GetEvaluationResultsAsync(HttpClient httpClient, Guid evaluationRunId, Guid claimId)
{
	using var response = await httpClient.GetAsync($"api/v1/evaluation-runs/{evaluationRunId}/claims/{claimId}/results");
	return response.IsSuccessStatusCode
		? await response.Content.ReadFromJsonAsync<EvaluationClaimResultsResponse>(JsonOptions.Default)
		: null;
}

static (string? verdict, decimal? riskScore) ExtractFraudResult(EvaluationClaimResultsResponse results)
{
	var output = results.AgentOutputs.LastOrDefault(item =>
		string.Equals(item.AgentId, "FraudDetectionAgent", StringComparison.OrdinalIgnoreCase))?.Output;
	if (output is not JsonElement fraudOutput || fraudOutput.ValueKind != JsonValueKind.Object)
	{
		return (null, null);
	}

	var verdict = GetJsonString(fraudOutput, "Verdict");
	return (verdict, GetJsonDecimal(fraudOutput, "RiskScore"));
}

static IReadOnlyList<EvaluationTokenUsage> ExtractTokenUsage(EvaluationClaimResultsResponse results) => results.AgentOutputs
	.Select(output => new
	{
		output.AgentId,
		TokenUsage = GetJsonObject(output.Output, "tokenUsage")
	})
	.Where(item => item.TokenUsage is JsonElement { ValueKind: JsonValueKind.Object })
	.Select(item => new EvaluationTokenUsage(
		item.AgentId,
		GetJsonInt64(item.TokenUsage!.Value, "InputTokenCount") ?? 0,
		GetJsonInt64(item.TokenUsage!.Value, "OutputTokenCount") ?? 0))
	.ToList();

static JsonElement? GetJsonObject(JsonElement element, string propertyName)
{
	if (element.ValueKind != JsonValueKind.Object)
	{
		return null;
	}

	var property = element.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
	return property.Value.ValueKind == JsonValueKind.Object ? property.Value : null;
}

static long? GetJsonInt64(JsonElement element, string propertyName)
{
	var property = element.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
	return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var value) ? value : null;
}

static string? GetJsonString(JsonElement element, string propertyName) =>
	element.EnumerateObject().FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)).Value.GetString();

static decimal? GetJsonDecimal(JsonElement element, string propertyName)
{
	var property = element.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
	return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var value) ? value : null;
}

static string ToIntakeFieldName(string fieldName) => fieldName switch
{
	"policyNumber" => "PolicyNumber",
	"claimType" => "ClaimType",
	"lossAmount" => "LossAmount",
	_ => fieldName
};

static bool IsTerminalStatus(string status) => status is
	"SETTLEMENT_APPROVED" or "SETTLEMENT_REJECTED" or "MANUAL_REVIEW_ASSIGNED" or "SLA_BREACHED";

static string NormalizeDecision(string status) => status switch
{
	"SETTLEMENT_APPROVED" => "APPROVE",
	"SETTLEMENT_REJECTED" => "REJECT",
	"MANUAL_REVIEW_ASSIGNED" or "SLA_BREACHED" => "MANUAL_REVIEW",
	_ => status
};

static Dictionary<string, string> ParseOptions(IEnumerable<string> arguments)
{
	var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	using var enumerator = arguments.GetEnumerator();
	while (enumerator.MoveNext())
	{
		var optionName = enumerator.Current;
		if (!optionName.StartsWith("--", StringComparison.Ordinal) || !enumerator.MoveNext())
		{
			continue;
		}

		parsed[optionName[2..]] = enumerator.Current;
	}

	return parsed;
}

internal sealed class EvaluationDataset
{
	public string Version { get; init; } = string.Empty;

	public List<EvaluationCase> Cases { get; init; } = [];
}

internal sealed class EvaluationCase
{
	public string ScenarioId { get; init; } = string.Empty;

	public string ScenarioType { get; init; } = string.Empty;

	public JsonElement ClaimInput { get; init; }

	public List<string> SupportingDocumentFixtures { get; init; } = [];

	public string ExpectedDecision { get; init; } = string.Empty;

	public string ExpectedFraudVerdict { get; init; } = string.Empty;

	public string ExpectedPolicyVerdict { get; init; } = string.Empty;

	public List<string> ExpectedNotificationEvents { get; init; } = [];

	public bool IsValid() =>
		!string.IsNullOrWhiteSpace(ScenarioId) &&
		!string.IsNullOrWhiteSpace(ScenarioType) &&
		ClaimInput.ValueKind == JsonValueKind.Object &&
		!string.IsNullOrWhiteSpace(ExpectedDecision) &&
		!string.IsNullOrWhiteSpace(ExpectedFraudVerdict) &&
		!string.IsNullOrWhiteSpace(ExpectedPolicyVerdict);
}

internal sealed class HarnessConfiguration
{
	public required Uri ApiBaseUri { get; init; }

	public required string ApiScope { get; init; }

	public required Uri ReportsContainerUri { get; init; }

	public bool UseManagedIdentity { get; init; }

	public string? ManagedIdentityClientId { get; init; }

	public string AuthenticationMode => UseManagedIdentity ? "ManagedIdentity" : "AzureCli";

	public static HarnessConfiguration FromEnvironment(string environment)
	{
		var baseUrl = Environment.GetEnvironmentVariable("CLAIM_SETTLEMENT_API_BASE_URL");
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			throw new InvalidOperationException("CLAIM_SETTLEMENT_API_BASE_URL must be set for the selected environment.");
		}

		var audience = Environment.GetEnvironmentVariable("CLAIM_SETTLEMENT_API_AUDIENCE") ?? "api://claim-settlement-api";
		var reportsContainerUri = Environment.GetEnvironmentVariable("CLAIM_SETTLEMENT_EVALUATION_REPORTS_CONTAINER_URI");
		if (!Uri.TryCreate(reportsContainerUri, UriKind.Absolute, out var parsedReportsContainerUri))
		{
			throw new InvalidOperationException("CLAIM_SETTLEMENT_EVALUATION_REPORTS_CONTAINER_URI must be an absolute Azure Blob container URI.");
		}

		var useManagedIdentity = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
		return new HarnessConfiguration
		{
			ApiBaseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute),
			ApiScope = audience.TrimEnd('/') + "/.default",
			ReportsContainerUri = parsedReportsContainerUri,
			UseManagedIdentity = useManagedIdentity,
			ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
		};
	}
}

internal static class AzureOpenAiTokenPricingConfiguration
{
	public static AzureOpenAiTokenPricing FromEnvironment()
	{
		const string inputRateName = "CLAIM_SETTLEMENT_OPENAI_INPUT_USD_PER_MILLION";
		const string outputRateName = "CLAIM_SETTLEMENT_OPENAI_OUTPUT_USD_PER_MILLION";
		if (!decimal.TryParse(Environment.GetEnvironmentVariable(inputRateName), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var inputRate) || inputRate < 0 ||
			!decimal.TryParse(Environment.GetEnvironmentVariable(outputRateName), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var outputRate) || outputRate < 0)
		{
			throw new InvalidOperationException($"Set {inputRateName} and {outputRateName} to the current USD price per million tokens for the deployed Azure OpenAI model.");
		}

		return new AzureOpenAiTokenPricing(inputRate, outputRate);
	}
}

internal sealed class EvaluationQualityGates
{
	public double MinimumAccuracy { get; init; } = 0.95d;

	public TimeSpan MaximumP95Latency { get; init; } = TimeSpan.FromSeconds(30);

	public static EvaluationQualityGates FromEnvironment()
	{
		var minimumAccuracy = ReadDouble("CLAIM_SETTLEMENT_MIN_ACCURACY", 0.95d);
		if (minimumAccuracy is < 0d or > 1d)
		{
			throw new InvalidOperationException("CLAIM_SETTLEMENT_MIN_ACCURACY must be between 0 and 1.");
		}

		var maximumP95LatencySeconds = ReadDouble("CLAIM_SETTLEMENT_MAX_P95_LATENCY_SECONDS", 30d);
		if (maximumP95LatencySeconds <= 0d)
		{
			throw new InvalidOperationException("CLAIM_SETTLEMENT_MAX_P95_LATENCY_SECONDS must be greater than zero.");
		}

		return new EvaluationQualityGates
		{
			MinimumAccuracy = minimumAccuracy,
			MaximumP95Latency = TimeSpan.FromSeconds(maximumP95LatencySeconds)
		};
	}

	private static double ReadDouble(string variableName, double defaultValue) =>
		double.TryParse(Environment.GetEnvironmentVariable(variableName), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
			? value
			: defaultValue;
}

internal sealed class IntakeConversationResponse
{
	public string SessionId { get; init; } = string.Empty;
}

internal sealed class IntakeCompletionResponse
{
	public Guid? ClaimId { get; init; }
}

internal sealed class ClaimStatusResponse
{
	public string Status { get; init; } = string.Empty;
}

internal sealed class EvaluationRunResponse
{
	public Guid RunId { get; init; }
}

internal sealed class EvaluationClaimResultsResponse
{
	public List<EvaluationAgentOutput> AgentOutputs { get; init; } = [];

	public List<EvaluationToolInvocationResponse> ToolInvocations { get; init; } = [];
}

internal sealed class EvaluationToolInvocationResponse
{
	public string AgentId { get; init; } = string.Empty;

	public string ToolName { get; init; } = string.Empty;

	public string Outcome { get; init; } = string.Empty;
}

internal sealed class EvaluationAgentOutput
{
	public string AgentId { get; init; } = string.Empty;

	public DateTime CreatedAtUtc { get; init; }

	public JsonElement Output { get; init; }
}

internal sealed class EvaluationSubmissionOutcome
{
	public required string ScenarioId { get; init; }

	public required string ExpectedDecision { get; init; }

	public required string ScenarioType { get; init; }

	public required string ExpectedFraudVerdict { get; init; }

	public string? ActualDecision { get; init; }

	public string? ActualFraudVerdict { get; init; }

	public decimal? FraudRiskScore { get; init; }

	public PipelineLatency? Latency { get; init; }

	public HallucinationCheckResult? HallucinationCheck { get; init; }

	public IReadOnlyList<ToolInvocationDiscrepancy> ToolDiscrepancies { get; init; } = Array.Empty<ToolInvocationDiscrepancy>();

	public IReadOnlyList<EvaluationTokenUsage> TokenUsage { get; init; } = Array.Empty<EvaluationTokenUsage>();

	public Guid? ClaimId { get; init; }

	public required bool Completed { get; init; }

	public required string Message { get; init; }

	public static EvaluationSubmissionOutcome Succeeded(EvaluationCase testCase, Guid claimId, string actualDecision, string status, string? actualFraudVerdict, decimal? fraudRiskScore, PipelineLatency latency, HallucinationCheckResult hallucinationCheck, IReadOnlyList<ToolInvocationDiscrepancy> toolDiscrepancies, IReadOnlyList<EvaluationTokenUsage> tokenUsage) => new()
	{
		ScenarioId = testCase.ScenarioId,
		ExpectedDecision = testCase.ExpectedDecision,
		ScenarioType = testCase.ScenarioType,
		ExpectedFraudVerdict = testCase.ExpectedFraudVerdict,
		ActualDecision = actualDecision,
		ActualFraudVerdict = actualFraudVerdict,
		FraudRiskScore = fraudRiskScore,
		Latency = latency,
		HallucinationCheck = hallucinationCheck,
		ToolDiscrepancies = toolDiscrepancies,
		TokenUsage = tokenUsage,
		ClaimId = claimId,
		Completed = true,
		Message = status
	};

	public static EvaluationSubmissionOutcome Failed(EvaluationCase testCase, string message) => new()
	{
		ScenarioId = testCase.ScenarioId,
		ExpectedDecision = testCase.ExpectedDecision,
		ScenarioType = testCase.ScenarioType,
		ExpectedFraudVerdict = testCase.ExpectedFraudVerdict,
		Completed = false,
		Message = message
	};
}

internal static class JsonOptions
{
	public static readonly JsonSerializerOptions Default = new() { PropertyNameCaseInsensitive = true };
}
