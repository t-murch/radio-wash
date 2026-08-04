using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class RestructureProcessedWebhookEventClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ProcessedWebhookEvents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "ProcessedWebhookEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ProcessedWebhookEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill claim state from the old boolean: true -> Succeeded (1), false ->
            // Failed (2). No row can legitimately be mid-Processing during a deploy, and
            // Failed keeps previously-failed events re-claimable by design.
            migrationBuilder.Sql(
                """
                UPDATE "ProcessedWebhookEvents"
                SET "Status" = CASE WHEN "IsSuccessful" THEN 1 ELSE 2 END,
                    "LastAttemptAt" = "ProcessedAt";
                """);

            migrationBuilder.DropColumn(
                name: "IsSuccessful",
                table: "ProcessedWebhookEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuccessful",
                table: "ProcessedWebhookEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "ProcessedWebhookEvents"
                SET "IsSuccessful" = ("Status" = 1);
                """);

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ProcessedWebhookEvents");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "ProcessedWebhookEvents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ProcessedWebhookEvents");
        }
    }
}
