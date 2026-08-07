using System.Net;
using ClaimSettlement.McpAdapters.Abstractions;
using ClaimSettlement.McpAdapters.Adapters;
using ClaimSettlement.McpAdapters.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaimSettlement.McpAdapters.Tests;

public class NotificationMcpAdapterTests
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly NotificationMcpAdapter _adapter;

    public NotificationMcpAdapterTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_mockHandler) { BaseAddress = new Uri("http://localhost") };
        _adapter = new NotificationMcpAdapter(httpClient, NullLogger<NotificationMcpAdapter>.Instance);
    }

    [Fact]
    public async Task InvokeAsync_SuccessfulNotification_ReturnsSuccess()
    {
        // Arrange
        var response = new NotifyResponse(true, "MSG-123", null);
        _mockHandler.EnqueueJsonResponse(response);
        
        var request = new NotifyRequest("USR1", "Email", "TMPL1", new Dictionary<string, string>(), "IDEMP-KEY-1");

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Success.Should().BeTrue();
        result.Result.MessageId.Should().Be("MSG-123");
    }

    [Fact]
    public async Task InvokeAsync_IdempotencyKey_IsForwardedAsHeader()
    {
        // Arrange
        var response = new NotifyResponse(true, "MSG-123", null);
        _mockHandler.EnqueueJsonResponse(response);
        
        var request = new NotifyRequest("USR1", "Email", "TMPL1", new Dictionary<string, string>(), "IDEMP-KEY-1");

        // Act
        await _adapter.InvokeAsync(request);

        // Assert
        var sentRequest = _mockHandler.SentRequests.Single();
        sentRequest.Headers.Contains("X-Idempotency-Key").Should().BeTrue();
        sentRequest.Headers.GetValues("X-Idempotency-Key").First().Should().Be("IDEMP-KEY-1");
    }

    [Fact]
    public async Task InvokeAsync_RetryBehavior_SucceedsAfterTransientFailures()
    {
        // Arrange
        var response = new NotifyResponse(true, "MSG-123", null);
        
        // 2 transient failures, then 1 success
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        _mockHandler.EnqueueJsonResponse(response);

        var request = new NotifyRequest("USR1", "Email", "TMPL1", new Dictionary<string, string>(), null);

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Result!.Success.Should().BeTrue();
        _mockHandler.SentRequests.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvokeAsync_ErrorMapping_ReturnsServiceUnavailable()
    {
        // Arrange
        // The adapter has 2 retries, so we need 3 failures total.
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);
        _mockHandler.EnqueueFailure(HttpStatusCode.InternalServerError);

        var request = new NotifyRequest("USR1", "Email", "TMPL1", new Dictionary<string, string>(), null);

        // Act
        var result = await _adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(McpErrorCode.ServiceUnavailable);
    }
}
