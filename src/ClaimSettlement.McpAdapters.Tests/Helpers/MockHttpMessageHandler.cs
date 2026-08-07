using System.Net;

namespace ClaimSettlement.McpAdapters.Tests.Helpers;

public class MockHttpMessageHandler : DelegatingHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly List<HttpRequestMessage> _sentRequests = new();

    public IReadOnlyList<HttpRequestMessage> SentRequests => _sentRequests;

    public void EnqueueResponse(HttpResponseMessage response) => _responses.Enqueue(response);

    // Convenience: enqueue a JSON success response
    public void EnqueueJsonResponse<T>(T content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(content,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        EnqueueResponse(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }

    // Convenience: enqueue a failure response
    public void EnqueueFailure(HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
    {
        EnqueueResponse(new HttpResponseMessage(statusCode));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _sentRequests.Add(request);
        if (_responses.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        return Task.FromResult(_responses.Dequeue());
    }
}
