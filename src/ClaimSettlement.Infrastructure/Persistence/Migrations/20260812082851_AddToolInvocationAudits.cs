using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimSettlement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddToolInvocationAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToolInvocationAudits",
                columns: table => new
                {
                    InvocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InvokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolInvocationAudits", x => x.InvocationId);
                    table.ForeignKey(
                        name: "FK_ToolInvocationAudits_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationAudits_ClaimId_AgentId_InvokedAtUtc",
                table: "ToolInvocationAudits",
                columns: new[] { "ClaimId", "AgentId", "InvokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolInvocationAudits_ProviderId_InvokedAtUtc",
                table: "ToolInvocationAudits",
                columns: new[] { "ProviderId", "InvokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolInvocationAudits");
        }
    }
}
