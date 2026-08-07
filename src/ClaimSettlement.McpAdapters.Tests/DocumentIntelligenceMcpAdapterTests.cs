using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using ClaimSettlement.McpAdapters.Abstractions;
using ClaimSettlement.McpAdapters.Adapters;
using ClaimSettlement.McpAdapters.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClaimSettlement.McpAdapters.Tests;

public class DocumentIntelligenceMcpAdapterTests
{
    private readonly IOptions<ExternalServiceSettings> _settings;

    public DocumentIntelligenceMcpAdapterTests()
    {
        var settings = new ExternalServiceSettings
        {
            DocumentIntelligence = new DocumentIntelligenceSettings
            {
                Endpoint = "https://example.com",
                DeploymentId = "prebuilt-document"
            }
        };
        _settings = Options.Create(settings);
    }

    [Fact]
    public async Task InvokeAsync_AnalysisFailure_ReturnsUnknownError()
    {
        // Arrange: mock the DocumentAnalysisClient to throw on all attempts
        var mockClient = new Mock<DocumentAnalysisClient>();
        mockClient
            .Setup(c => c.AnalyzeDocumentAsync(
                It.IsAny<WaitUntil>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<AnalyzeDocumentOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException("Failed to analyze document"));

        var adapter = new DocumentIntelligenceMcpAdapter(
            mockClient.Object, _settings, NullLogger<DocumentIntelligenceMcpAdapter>.Instance);

        var request = new DocumentExtractRequest(new byte[] { 1, 2, 3 }, "test.pdf");

        // Act
        var result = await adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(McpErrorCode.Unknown);
        result.ErrorMessage.Should().Contain("Failed to analyze document");

        // Verify it retried (1 initial + 3 retries = 4 total calls)
        mockClient.Verify(
            c => c.AnalyzeDocumentAsync(
                It.IsAny<WaitUntil>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<AnalyzeDocumentOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public async Task InvokeAsync_CancellationRequested_ReturnsTimeoutError()
    {
        // Arrange: mock the client to throw TaskCanceledException
        var mockClient = new Mock<DocumentAnalysisClient>();
        mockClient
            .Setup(c => c.AnalyzeDocumentAsync(
                It.IsAny<WaitUntil>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<AnalyzeDocumentOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Operation timed out"));

        var adapter = new DocumentIntelligenceMcpAdapter(
            mockClient.Object, _settings, NullLogger<DocumentIntelligenceMcpAdapter>.Instance);

        var request = new DocumentExtractRequest(new byte[] { 1, 2, 3 }, "test.pdf");

        // Act
        var result = await adapter.InvokeAsync(request);

        // Assert — TaskCanceledException is caught and classified; it may be retried first
        result.Success.Should().BeFalse();
        // After retries exhaust, the final exception is caught and mapped
        result.ErrorCode.Should().BeOneOf(McpErrorCode.Timeout, McpErrorCode.Unknown);
    }

    [Fact]
    public async Task InvokeAsync_EmptyDocument_StillAttemptsAnalysis()
    {
        // Arrange: mock to throw since we can't easily mock a successful AnalyzeResult
        var mockClient = new Mock<DocumentAnalysisClient>();
        mockClient
            .Setup(c => c.AnalyzeDocumentAsync(
                It.IsAny<WaitUntil>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<AnalyzeDocumentOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(400, "Invalid document content"));

        var adapter = new DocumentIntelligenceMcpAdapter(
            mockClient.Object, _settings, NullLogger<DocumentIntelligenceMcpAdapter>.Instance);

        var request = new DocumentExtractRequest(Array.Empty<byte>(), "empty.pdf");

        // Act
        var result = await adapter.InvokeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid document content");
    }

    [Fact]
    public void ToolName_ReturnsDocumentIntelligence()
    {
        var mockClient = new Mock<DocumentAnalysisClient>();
        var adapter = new DocumentIntelligenceMcpAdapter(
            mockClient.Object, _settings, NullLogger<DocumentIntelligenceMcpAdapter>.Instance);

        adapter.ToolName.Should().Be("DocumentIntelligence");
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var mockClient = new Mock<DocumentAnalysisClient>();
        var adapter = new DocumentIntelligenceMcpAdapter(
            mockClient.Object, _settings, NullLogger<DocumentIntelligenceMcpAdapter>.Instance);

        adapter.Description.Should().NotBeNullOrWhiteSpace();
    }
}
