namespace ClaimSettlement.Infrastructure.Persistence;

public interface IProviderSqlSessionContext
{
    string? ProviderId { get; }

    IDisposable Begin(string? providerId);
}