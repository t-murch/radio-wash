using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniqueTrackMappingPerJobSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical duplicate rows (one per playlist occurrence of a song, written
            // before the application deduplicated mapping writes) must go before the unique
            // index can exist. Per (JobId, SourceTrackId) the surviving row is the one
            // carrying a clean match, then the oldest.
            migrationBuilder.Sql("""
                DELETE FROM "TrackMappings"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "JobId", "SourceTrackId"
                            ORDER BY "HasCleanMatch" DESC, "Id" ASC
                        ) AS row_num
                        FROM "TrackMappings"
                    ) ranked
                    WHERE row_num > 1
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_TrackMappings_JobId",
                table: "TrackMappings");

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_JobId_SourceTrackId",
                table: "TrackMappings",
                columns: new[] { "JobId", "SourceTrackId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackMappings_JobId_SourceTrackId",
                table: "TrackMappings");

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_JobId",
                table: "TrackMappings",
                column: "JobId");
        }
    }
}
