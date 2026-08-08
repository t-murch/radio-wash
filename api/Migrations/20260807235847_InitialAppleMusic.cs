using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialAppleMusic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWebhookEvents", x => x.Id);
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
                    StripePriceId = table.Column<string>(type: "text", nullable: true),
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
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupabaseId = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PrimaryProvider = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookRetries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Signature = table.Column<string>(type: "text", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookRetries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CleanPlaylistJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "apple_music"),
                    TargetProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "apple_music"),
                    JobType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "clean"),
                    SwapExplicitForClean = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SourcePlaylistId = table.Column<string>(type: "text", nullable: false),
                    SourcePlaylistName = table.Column<string>(type: "text", nullable: false),
                    TargetPlaylistId = table.Column<string>(type: "text", nullable: true),
                    TargetPlaylistName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    TotalTracks = table.Column<int>(type: "integer", nullable: false),
                    ProcessedTracks = table.Column<int>(type: "integer", nullable: false),
                    MatchedTracks = table.Column<int>(type: "integer", nullable: false),
                    CurrentBatch = table.Column<string>(type: "text", nullable: true),
                    BatchSize = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CleanPlaylistJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CleanPlaylistJobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMusicTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "text", nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Scopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProviderMetadata = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RefreshFailureCount = table.Column<int>(type: "integer", nullable: false),
                    LastRefreshAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMusicTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMusicTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProviderData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    ProviderMetadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProviderData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProviderData_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
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
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
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
                    AutoDisabledReason = table.Column<string>(type: "text", nullable: true),
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
                name: "TrackMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    SourceTrackId = table.Column<string>(type: "text", nullable: false),
                    SourceTrackName = table.Column<string>(type: "text", nullable: false),
                    SourceArtistName = table.Column<string>(type: "text", nullable: false),
                    IsExplicit = table.Column<bool>(type: "boolean", nullable: false),
                    TargetTrackId = table.Column<string>(type: "text", nullable: true),
                    TargetTrackName = table.Column<string>(type: "text", nullable: true),
                    TargetArtistName = table.Column<string>(type: "text", nullable: true),
                    HasCleanMatch = table.Column<bool>(type: "boolean", nullable: false),
                    Isrc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MatchMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackMappings_CleanPlaylistJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "CleanPlaylistJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "IX_CleanPlaylistJobs_UserId",
                table: "CleanPlaylistJobs",
                column: "UserId");

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
                name: "IX_ProcessedWebhookEvents_EventId",
                table: "ProcessedWebhookEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhookEvents_ProcessedAt",
                table: "ProcessedWebhookEvents",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Name",
                table: "SubscriptionPlans",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_JobId",
                table: "TrackMappings",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMusicTokens_UserId_Provider",
                table: "UserMusicTokens",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProviderData_Provider_ProviderId",
                table: "UserProviderData",
                columns: new[] { "Provider", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProviderData_UserId",
                table: "UserProviderData",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SupabaseId",
                table: "Users",
                column: "SupabaseId",
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

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRetries_EventId",
                table: "WebhookRetries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRetries_NextRetryAt",
                table: "WebhookRetries",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookRetries_Status_NextRetryAt",
                table: "WebhookRetries",
                columns: new[] { "Status", "NextRetryAt" });

            // Mirror a Supabase auth signup into our own Users table. Hand-written because the
            // scaffolder cannot generate triggers, and it targets auth.users — a schema Supabase
            // owns, which vanilla Postgres does not have. Integration tests stub that schema in
            // before migrating; see PostgreSqlIntegrationTestBase.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.handle_new_auth_user()
                RETURNS TRIGGER
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = ''
                AS $$
                BEGIN
                    INSERT INTO public."Users" ("SupabaseId", "DisplayName", "Email", "CreatedAt", "UpdatedAt")
                    VALUES (
                        NEW.id::text,
                        COALESCE(NEW.raw_user_meta_data ->> 'full_name', NEW.raw_user_meta_data ->> 'name', NEW.email),
                        NEW.email,
                        NOW(),
                        NOW()
                    );
                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER on_auth_user_created
                    AFTER INSERT ON auth.users
                    FOR EACH ROW
                    EXECUTE FUNCTION public.handle_new_auth_user();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.handle_new_auth_user();");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "PlaylistSyncHistory");

            migrationBuilder.DropTable(
                name: "ProcessedWebhookEvents");

            migrationBuilder.DropTable(
                name: "TrackMappings");

            migrationBuilder.DropTable(
                name: "UserMusicTokens");

            migrationBuilder.DropTable(
                name: "UserProviderData");

            migrationBuilder.DropTable(
                name: "UserSubscriptions");

            migrationBuilder.DropTable(
                name: "WebhookRetries");

            migrationBuilder.DropTable(
                name: "PlaylistSyncConfigs");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "CleanPlaylistJobs");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
