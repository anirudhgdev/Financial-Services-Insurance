using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimSettlement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EvaluationRunId",
                table: "Claims",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvaluationRuns",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DatasetVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationRuns", x => x.RunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_EvaluationRunId",
                table: "Claims",
                column: "EvaluationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ProviderId_EvaluationRunId",
                table: "Claims",
                columns: new[] { "ProviderId", "EvaluationRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationRuns_ProviderId_CreatedByUserId_RunId",
                table: "EvaluationRuns",
                columns: new[] { "ProviderId", "CreatedByUserId", "RunId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Claims_EvaluationRuns_EvaluationRunId",
                table: "Claims",
                column: "EvaluationRunId",
                principalTable: "EvaluationRuns",
                principalColumn: "RunId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Claims_EvaluationRuns_EvaluationRunId",
                table: "Claims");

            migrationBuilder.DropTable(
                name: "EvaluationRuns");

            migrationBuilder.DropIndex(
                name: "IX_Claims_EvaluationRunId",
                table: "Claims");

            migrationBuilder.DropIndex(
                name: "IX_Claims_ProviderId_EvaluationRunId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "EvaluationRunId",
                table: "Claims");
        }
    }
}
