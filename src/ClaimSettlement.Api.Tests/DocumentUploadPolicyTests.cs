using ClaimSettlement.Api.Claims;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class DocumentUploadPolicyTests
{
    [Fact]
    public void RejectsUnsupportedExtension()
    {
        var policy = new DocumentUploadPolicy();
        var file = BuildFile("evidence.exe", "application/octet-stream", 1024);

        var result = policy.Validate(file, 0);

        Assert.False(result.IsValid);
        Assert.Contains("Unsupported file extension", result.Error);
    }

    [Fact]
    public void RejectsWhenCountLimitReached()
    {
        var policy = new DocumentUploadPolicy();
        var file = BuildFile("evidence.pdf", "application/pdf", 1024);

        var result = policy.Validate(file, 10);

        Assert.False(result.IsValid);
        Assert.Contains("Maximum of 10 documents", result.Error);
    }

    [Fact]
    public void RejectsFilesOver50Mb()
    {
        var policy = new DocumentUploadPolicy();
        var file = BuildFile("evidence.pdf", "application/pdf", (50L * 1024L * 1024L) + 1);

        var result = policy.Validate(file, 0);

        Assert.False(result.IsValid);
        Assert.Contains("50 MB", result.Error);
    }

    private static IFormFile BuildFile(string name, string contentType, long size)
    {
        var stream = new MemoryStream(new byte[Math.Min(size, 1024)]);
        var formFile = new FormFile(stream, 0, size, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        return formFile;
    }
}
