using System.Net;
using ClaimSettlement.McpAdapters.Abstractions;
using ClaimSettlement.McpAdapters.Adapters;
using ClaimSettlement.McpAdapters.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaimSettlement.McpAdapters.Tests;

public class PolicyManagementMcpAdapterTests
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly PolicyManagementMcpAdapter _adapter;

    public PolicyManagementMcpAdapterTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_mockHandler) { BaseAddress = new Uri("http://localhost") };
        _adapter = new PolicyManagementMcpAdapter(httpClient, NullLogger<PolicyManagementMcpAdapter>.Instance);
    }

    [Fact]
    public async Task InvokeAsync_SuccessfulLookup_ReturnsPolicy()
    {
        // Arrange
        var policy = new PolicyDetailsDto("POL123", "John Doe", "ID", DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1), "Active", "Auto", Array.Empty<CoverageDetailDto>(), Array.Empty<string>(), 500, 1000, DateTimeOffset.Now, DateTimeOffset.Now);
        _mockHandler.EnqueueJsonResponse(policy);
        
        var request = new PolicyLookupRequest("POL123");

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Found.Should().BeTrue();
        result.Result.Policy.Should().BeEquivalentTo(policy);
    }

    [Fact]
    public async Task InvokeAsync_RetryBehavior_SucceedsAfterTransientFailures()
    {
        // Arrange
        var policy = new PolicyDetailsDto("POL123", "John Doe", "ID", DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1), "Active", "Auto", Array.Empty<CoverageDetailDto>(), Array.Empty<string>(), 500, 1000, DateTimeOffset.Now, DateTimeOffset.Now);
        
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        _mockHandler.EnqueueJsonResponse(policy);

        var request = new PolicyLookupRequest("POL123");

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Result!.Found.Should().BeTrue();
        _mockHandler.SentRequests.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvokeAsync_CircuitBreaker_OpensAfterFailures()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        }

        var request = new PolicyLookupRequest("POL123");

        // Act 1: Should exhaust retries and fail. Since minimum throughput is 3, 
        // 1 initial + 2 retries = 3 failed attempts, which trips the circuit for the next request.
        await _adapter.InvokeAsync(request);
        
        // Act 2: Should hit open circuit
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(McpErrorCode.CircuitOpen);
    }

    [Fact]
    public async Task InvokeAsync_NotFound_ReturnsFoundFalse()
    {
        // Arrange
        _mockHandler.EnqueueFailure(HttpStatusCode.NotFound);
        _mockHandler.EnqueueFailure(HttpStatusCode.NotFound);
        _mockHandler.EnqueueFailure(HttpStatusCode.NotFound);

        var request = new PolicyLookupRequest("POL123");

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Result!.Found.Should().BeFalse();
        result.Result.ErrorMessage.Should().Be("Policy not found");
    }

    [Fact]
    public async Task InvokeAsync_InvalidPolicyNumber_ReturnsInvalidInput()
    {
        // Arrange
        var request = new PolicyLookupRequest(""); 

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(McpErrorCode.InvalidInput);
    }
}
