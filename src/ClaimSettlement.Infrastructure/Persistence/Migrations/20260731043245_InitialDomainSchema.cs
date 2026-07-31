using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimSettlement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDomainSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClaimantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DateOfLoss = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "ProviderConfigurations",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ManualReviewFraudThreshold = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    ManualReviewClaimAmountThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeduplicationWindowDays = table.Column<int>(type: "int", nullable: false),
                    InformationRequestDeadlineDays = table.Column<int>(type: "int", nullable: false),
                    AdjusterSlaPeriodHours = table.Column<int>(type: "int", nullable: false),
                    SupportedClaimTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupportedNotificationChannels = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PipelineConcurrencyLimit = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ClaimTypeMandatoryFields = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoverageMappingRules = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExclusionSets = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AlwaysManualClaimTypes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderConfigurations", x => x.ProviderId);
                    table.CheckConstraint("CK_ProviderConfiguration_AlwaysManualClaimTypes_IsJson", "ISJSON([AlwaysManualClaimTypes]) = 1");
                    table.CheckConstraint("CK_ProviderConfiguration_ClaimTypeMandatoryFields_IsJson", "ISJSON([ClaimTypeMandatoryFields]) = 1");
                    table.CheckConstraint("CK_ProviderConfiguration_CoverageMappingRules_IsJson", "ISJSON([CoverageMappingRules]) = 1");
                    table.CheckConstraint("CK_ProviderConfiguration_ExclusionSets_IsJson", "ISJSON([ExclusionSets]) = 1");
                    table.CheckConstraint("CK_ProviderConfiguration_FraudThreshold_Range", "[ManualReviewFraudThreshold] >= 0.30 AND [ManualReviewFraudThreshold] <= 0.90");
                    table.CheckConstraint("CK_ProviderConfiguration_SupportedClaimTypes_IsJson", "ISJSON([SupportedClaimTypes]) = 1");
                    table.CheckConstraint("CK_ProviderConfiguration_SupportedNotificationChannels_IsJson", "ISJSON([SupportedNotificationChannels]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "AdjusterAssignments",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjusterId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Rationale = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SettlementOverride = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjusterAssignments", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_AdjusterAssignments_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentOutputs",
                columns: table => new
                {
                    OutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OutputPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentOutputs", x => x.OutputId);
                    table.CheckConstraint("CK_AgentOutput_OutputPayload_IsJson", "ISJSON([OutputPayload]) = 1");
                    table.ForeignKey(
                        name: "FK_AgentOutputs_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.EntryId);
                    table.CheckConstraint("CK_AuditLog_Payload_IsJson", "ISJSON([Payload]) = 1");
                    table.ForeignKey(
                        name: "FK_AuditLogs_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId");
                });

            migrationBuilder.CreateTable(
                name: "ClaimPipelineStates",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentStep = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CompletedSteps = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgentOutputs = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimPipelineStates", x => x.ClaimId);
                    table.CheckConstraint("CK_ClaimPipelineState_AgentOutputs_IsJson", "ISJSON([AgentOutputs]) = 1");
                    table.CheckConstraint("CK_ClaimPipelineState_CompletedSteps_IsJson", "ISJSON([CompletedSteps]) = 1");
                    table.ForeignKey(
                        name: "FK_ClaimPipelineStates_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdjusterAssignments_AdjusterId_AssignedAt",
                table: "AdjusterAssignments",
                columns: new[] { "AdjusterId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdjusterAssignments_ClaimId_AssignedAt",
                table: "AdjusterAssignments",
                columns: new[] { "ClaimId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentOutputs_ClaimId_AgentId_CreatedAt",
                table: "AgentOutputs",
                columns: new[] { "ClaimId", "AgentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ClaimId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "ClaimId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ProviderId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "ProviderId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimPipelineStates_ProviderId_Status",
                table: "ClaimPipelineStates",
                columns: new[] { "ProviderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ProviderId_PolicyNumber_DateOfLoss",
                table: "Claims",
                columns: new[] { "ProviderId", "PolicyNumber", "DateOfLoss" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ProviderId_Status_CreatedAt",
                table: "Claims",
                columns: new[] { "ProviderId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderConfigurations_IsActive",
                table: "ProviderConfigurations",
                column: "IsActive");

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'security')
                BEGIN
                    EXEC('CREATE SCHEMA [security] AUTHORIZATION [dbo]')
                END
                """);

            migrationBuilder.Sql("""
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
                )
                """);

            migrationBuilder.Sql("""
                CREATE SECURITY POLICY [security].[ProviderIsolationPolicy]
                    ADD FILTER PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[Claims],
                    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[Claims] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[Claims] BEFORE UPDATE,
                    ADD FILTER PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[ClaimPipelineStates],
                    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[ClaimPipelineStates] AFTER INSERT,
                    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[ClaimPipelineStates] BEFORE UPDATE,
                    ADD FILTER PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[AuditLogs],
                    ADD BLOCK PREDICATE [security].[fn_provider_access_predicate]([ProviderId]) ON [dbo].[AuditLogs] AFTER INSERT
                WITH (STATE = ON)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'ProviderIsolationPolicy')
                BEGIN
                    DROP SECURITY POLICY [security].[ProviderIsolationPolicy]
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[security].[fn_provider_access_predicate]', N'IF') IS NOT NULL
                BEGIN
                    DROP FUNCTION [security].[fn_provider_access_predicate]
                END
                """);

            migrationBuilder.DropTable(
                name: "AdjusterAssignments");

            migrationBuilder.DropTable(
                name: "AgentOutputs");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ClaimPipelineStates");

            migrationBuilder.DropTable(
                name: "ProviderConfigurations");

            migrationBuilder.DropTable(
                name: "Claims");
        }
    }
}
