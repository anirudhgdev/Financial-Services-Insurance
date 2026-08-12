using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimSettlement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderUserAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderConfigSnapshot",
                table: "ClaimPipelineStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderId",
                table: "AdjusterAssignments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ProviderUserMemberships",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FirstAccessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderUserMemberships", x => new { x.ProviderId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "ProviderUserRoles",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderUserRoles", x => new { x.ProviderId, x.UserId, x.Role });
                    table.ForeignKey(
                        name: "FK_ProviderUserRoles_ProviderUserMemberships_ProviderId_UserId",
                        columns: x => new { x.ProviderId, x.UserId },
                        principalTable: "ProviderUserMemberships",
                        principalColumns: new[] { "ProviderId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClaimPipelineState_ProviderConfigSnapshot_IsJson",
                table: "ClaimPipelineStates",
                sql: "ISJSON([ProviderConfigSnapshot]) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AdjusterAssignments_ProviderId_AssignedAt",
                table: "AdjusterAssignments",
                columns: new[] { "ProviderId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderUserMemberships_ProviderId_LastAccessedAt",
                table: "ProviderUserMemberships",
                columns: new[] { "ProviderId", "LastAccessedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderUserRoles");

            migrationBuilder.DropTable(
                name: "ProviderUserMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClaimPipelineState_ProviderConfigSnapshot_IsJson",
                table: "ClaimPipelineStates");

            migrationBuilder.DropIndex(
                name: "IX_AdjusterAssignments_ProviderId_AssignedAt",
                table: "AdjusterAssignments");

            migrationBuilder.DropColumn(
                name: "ProviderConfigSnapshot",
                table: "ClaimPipelineStates");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "AdjusterAssignments");
        }
    }
}
