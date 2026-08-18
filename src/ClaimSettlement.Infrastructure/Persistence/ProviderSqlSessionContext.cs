using System.Threading;

namespace ClaimSettlement.Infrastructure.Persistence;

public sealed class ProviderSqlSessionContext : IProviderSqlSessionContext
{
    private readonly AsyncLocal<string?> _providerId = new();

    public string? ProviderId => _providerId.Value;

    public IDisposable Begin(string? providerId)
    {
        var previousProviderId = _providerId.Value;
        _providerId.Value = string.IsNullOrWhiteSpace(providerId) ? null : providerId;
        return new RestoreScope(_providerId, previousProviderId);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly AsyncLocal<string?> _providerId;
        private readonly string? _previousProviderId;
        private bool _disposed;

        public RestoreScope(AsyncLocal<string?> providerId, string? previousProviderId)
        {
            _providerId = providerId;
            _previousProviderId = previousProviderId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _providerId.Value = _previousProviderId;
            _disposed = true;
        }
    }
}