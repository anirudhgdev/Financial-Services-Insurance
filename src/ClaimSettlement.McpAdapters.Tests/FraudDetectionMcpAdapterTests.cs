using System.Net;
using ClaimSettlement.McpAdapters.Abstractions;
using ClaimSettlement.McpAdapters.Adapters;
using ClaimSettlement.McpAdapters.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaimSettlement.McpAdapters.Tests;

public class FraudDetectionMcpAdapterTests
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly FraudDetectionMcpAdapter _adapter;

    public FraudDetectionMcpAdapterTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_mockHandler) { BaseAddress = new Uri("http://localhost") };
        _adapter = new FraudDetectionMcpAdapter(httpClient, NullLogger<FraudDetectionMcpAdapter>.Instance);
    }

    [Fact]
    public async Task InvokeAsync_SuccessfulScoring_ReturnsFraudScore()
    {
        // Arrange
        var scoreResponse = new FraudScoreResponse(0.15m, "Low", Array.Empty<FraudSignalDto>(), true);
        _mockHandler.EnqueueJsonResponse(scoreResponse);
        
        var request = new FraudScoreRequest("CLM123", "POL123", "CLMT123", "Auto", 5000, DateTimeOffset.Now, "Description");

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Score.Should().Be(0.15m);
        result.Result.RiskLevel.Should().Be("Low");
    }

    [Fact]
    public async Task InvokeAsync_CircuitBreaker_OpensAfterFailures()
    {
        // Arrange
        // The fraud pipeline uses minimum throughput 5 and failure ratio 0.5.
        // There are no retries, so 5 requests should trip the breaker if all fail.
        for (int i = 0; i < 5; i++)
        {
            _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        }

        var request = new FraudScoreRequest("CLM123", "POL123", "CLMT123", "Auto", 5000, DateTimeOffset.Now, "Description");

        // Act 1: 5 failures to trip the circuit
        for (int i = 0; i < 5; i++)
        {
            await _adapter.InvokeAsync(request);
        }
        
        // Act 2: Next request should hit open circuit
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(McpErrorCode.CircuitOpen);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task InvokeAsync_ErrorMapping_ReturnsServiceUnavailable(HttpStatusCode statusCode)
    {
        // Arrange
        _mockHandler.EnqueueFailure(statusCode);
        var request = new FraudScoreRequest("CLM123", "POL123", "CLMT123", "Auto", 5000, DateTimeOffset.Now, "Description");

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(McpErrorCode.ServiceUnavailable);
    }
}
