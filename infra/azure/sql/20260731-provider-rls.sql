-- Provider-scoped row-level security for claim/audit tables.
-- Before running this script, apply EF migrations so the target tables exist.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'security')
BEGIN
    EXEC('CREATE SCHEMA [security] AUTHORIZATION [dbo]');
END
GO

IF OBJECT_ID(N'[security].[fn_provider_access_predicate]', N'IF') IS NOT NULL
BEGIN
    DROP FUNCTION [security].[fn_provider_access_predicate];
END
GO

CREATE FUNCTION [security].[fn_provider_access_predicate](@ProviderId NVARCHAR(64))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
(
    SELECT 1 AS [fn_access_result]
    WHERE
        @ProviderId = CAST(SESSION_CONTEXT(N'ProviderId') AS NVARCHAR(64))
        OR IS_MEMBER('db_owner') = 1
);
GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'ProviderIsolationPolicy')
BEGIN
    DROP SECURITY POLICY [security].[ProviderIsolationPolicy];
END
GO

CREATE SECURITY POLICY [security].[ProviderIsolationPolicy]
    ADD FILTER PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[Claims],
    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[Claims] AFTER INSERT,
    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[Claims] BEFORE UPDATE,
    ADD FILTER PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[ClaimPipelineStates],
    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[ClaimPipelineStates] AFTER INSERT,
    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[ClaimPipelineStates] BEFORE UPDATE,
    ADD FILTER PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[AuditLogs],
    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[AuditLogs] AFTER INSERT
WITH (STATE = ON);
GO

-- Application sessions should set provider context before data access:
-- EXEC sp_set_session_context @key = N'ProviderId', @value = N'<provider-id>';
