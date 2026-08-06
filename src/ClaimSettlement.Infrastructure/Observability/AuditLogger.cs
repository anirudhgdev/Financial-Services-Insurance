using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Persistence;
using System.Text.Json;

namespace ClaimSettlement.Infrastructure.Observability;

public sealed class AuditLogEntry
{
    public Guid? ClaimId { get; init; }

    public required string ProviderId { get; init; }

    public required string EventType { get; init; }

    public required string ActorId { get; init; }

    public required string ActorType { get; init; }

    public object Payload { get; init; } = new { };
}

public interface IAuditLogger
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken ct);

    Task UpdateAsync(Guid entryId, object payload, CancellationToken ct);

    Task DeleteAsync(Guid entryId, CancellationToken ct);
}

public sealed class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ClaimSettlementDbContext _dbContext;

    public AuditLogger(ClaimSettlementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.EventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ActorType);

        _dbContext.AuditLogs.Add(new AuditLog
        {
            EntryId = Guid.NewGuid(),
            ClaimId = entry.ClaimId,
            ProviderId = entry.ProviderId,
            EventType = entry.EventType,
            ActorId = entry.ActorId,
            ActorType = entry.ActorType,
            Payload = JsonSerializer.Serialize(entry.Payload, SerializerOptions),
            Timestamp = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(Guid entryId, object payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("AuditLog is append-only. Update operations are not allowed.");
    }

    public Task DeleteAsync(Guid entryId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("AuditLog is append-only. Delete operations are not allowed.");
    }
}
