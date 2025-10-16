using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RadioWash.Api.Migrations
{
  /// <inheritdoc />
  public partial class AddSubscriptionTables : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
          name: "PlaylistSyncConfigs",
          columns: table => new
          {
            Id = table.Column<int>(type: "integer", nullable: false)
                  .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            UserId = table.Column<int>(type: "integer", nullable: false),
            OriginalJobId = table.Column<int>(type: "integer", nullable: false),
            SourcePlaylistId = table.Column<string>(type: "text", nullable: false),
            TargetPlaylistId = table.Column<string>(type: "text", nullable: false),
            IsActive = table.Column<bool>(type: "boolean", nullable: false),
            SyncFrequency = table.Column<string>(type: "text", nullable: false),
            LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            LastSyncStatus = table.Column<string>(type: "text", nullable: true),
            LastSyncError = table.Column<string>(type: "text", nullable: true),
            NextScheduledSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            SyncStats = table.Column<string>(type: "text", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_PlaylistSyncConfigs", x => x.Id);
            table.ForeignKey(
                      name: "FK_PlaylistSyncConfigs_CleanPlaylistJobs_OriginalJobId",
                      column: x => x.OriginalJobId,
                      principalTable: "CleanPlaylistJobs",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
            table.ForeignKey(
                      name: "FK_PlaylistSyncConfigs_Users_UserId",
                      column: x => x.UserId,
                      principalTable: "Users",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateTable(
          name: "SubscriptionPlans",
          columns: table => new
          {
            Id = table.Column<int>(type: "integer", nullable: false)
                  .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            Name = table.Column<string>(type: "text", nullable: false),
            PriceInCents = table.Column<int>(type: "integer", nullable: false),
            BillingPeriod = table.Column<string>(type: "text", nullable: false),
            MaxPlaylists = table.Column<int>(type: "integer", nullable: true),
            MaxTracksPerPlaylist = table.Column<int>(type: "integer", nullable: true),
            Features = table.Column<string>(type: "text", nullable: false),
            IsActive = table.Column<bool>(type: "boolean", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "PlaylistSyncHistory",
          columns: table => new
          {
            Id = table.Column<int>(type: "integer", nullable: false)
                  .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            SyncConfigId = table.Column<int>(type: "integer", nullable: false),
            StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            Status = table.Column<string>(type: "text", nullable: false),
            TracksAdded = table.Column<int>(type: "integer", nullable: false),
            TracksRemoved = table.Column<int>(type: "integer", nullable: false),
            TracksUnchanged = table.Column<int>(type: "integer", nullable: false),
            ErrorMessage = table.Column<string>(type: "text", nullable: true),
            ExecutionTimeMs = table.Column<int>(type: "integer", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_PlaylistSyncHistory", x => x.Id);
            table.ForeignKey(
                      name: "FK_PlaylistSyncHistory_PlaylistSyncConfigs_SyncConfigId",
                      column: x => x.SyncConfigId,
                      principalTable: "PlaylistSyncConfigs",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateTable(
          name: "UserSubscriptions",
          columns: table => new
          {
            Id = table.Column<int>(type: "integer", nullable: false)
                  .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            UserId = table.Column<int>(type: "integer", nullable: false),
            PlanId = table.Column<int>(type: "integer", nullable: false),
            StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
            StripeCustomerId = table.Column<string>(type: "text", nullable: true),
            Status = table.Column<string>(type: "text", nullable: false),
            CurrentPeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            CanceledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
            table.ForeignKey(
                      name: "FK_UserSubscriptions_SubscriptionPlans_PlanId",
                      column: x => x.PlanId,
                      principalTable: "SubscriptionPlans",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
            table.ForeignKey(
                      name: "FK_UserSubscriptions_Users_UserId",
                      column: x => x.UserId,
                      principalTable: "Users",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateIndex(
          name: "IX_PlaylistSyncConfigs_NextScheduledSync",
          table: "PlaylistSyncConfigs",
          column: "NextScheduledSync");

      migrationBuilder.CreateIndex(
          name: "IX_PlaylistSyncConfigs_OriginalJobId",
          table: "PlaylistSyncConfigs",
          column: "OriginalJobId");

      migrationBuilder.CreateIndex(
          name: "IX_PlaylistSyncConfigs_UserId",
          table: "PlaylistSyncConfigs",
          column: "UserId");

      migrationBuilder.CreateIndex(
          name: "IX_PlaylistSyncConfigs_UserId_OriginalJobId",
          table: "PlaylistSyncConfigs",
          columns: new[] { "UserId", "OriginalJobId" },
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_PlaylistSyncHistory_StartedAt",
          table: "PlaylistSyncHistory",
          column: "StartedAt");

      migrationBuilder.CreateIndex(
          name: "IX_PlaylistSyncHistory_SyncConfigId",
          table: "PlaylistSyncHistory",
          column: "SyncConfigId");

      migrationBuilder.CreateIndex(
          name: "IX_SubscriptionPlans_Name",
          table: "SubscriptionPlans",
          column: "Name",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_UserSubscriptions_PlanId",
          table: "UserSubscriptions",
          column: "PlanId");

      migrationBuilder.CreateIndex(
          name: "IX_UserSubscriptions_Status",
          table: "UserSubscriptions",
          column: "Status");

      migrationBuilder.CreateIndex(
          name: "IX_UserSubscriptions_StripeSubscriptionId",
          table: "UserSubscriptions",
          column: "StripeSubscriptionId",
          unique: true,
          filter: "\"StripeSubscriptionId\" IS NOT NULL");

      migrationBuilder.CreateIndex(
          name: "IX_UserSubscriptions_UserId",
          table: "UserSubscriptions",
          column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "PlaylistSyncHistory");

      migrationBuilder.DropTable(
          name: "UserSubscriptions");

      migrationBuilder.DropTable(
          name: "PlaylistSyncConfigs");

      migrationBuilder.DropTable(
          name: "SubscriptionPlans");
    }
  }
}
