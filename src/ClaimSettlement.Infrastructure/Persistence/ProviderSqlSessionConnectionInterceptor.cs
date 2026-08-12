using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data;
using System.Data.Common;

namespace ClaimSettlement.Infrastructure.Persistence;

public sealed class ProviderSqlSessionConnectionInterceptor : DbConnectionInterceptor
{
    private readonly IProviderSqlSessionContext _sessionContext;

    public ProviderSqlSessionConnectionInterceptor(IProviderSqlSessionContext sessionContext)
    {
        _sessionContext = sessionContext;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetProviderSessionContext(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetProviderSessionContextAsync(connection, cancellationToken);
    }

    private void SetProviderSessionContext(DbConnection connection)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task SetProviderSessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'ProviderId', @value=@providerId;";

        var providerId = command.CreateParameter();
        providerId.ParameterName = "@providerId";
        providerId.DbType = DbType.String;
        providerId.Value = (object?)_sessionContext.ProviderId ?? DBNull.Value;
        command.Parameters.Add(providerId);

        return command;
    }
}