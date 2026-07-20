using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossServiceCopyJobFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Isrc",
                table: "TrackMappings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchMethod",
                table: "TrackMappings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobType",
                table: "CleanPlaylistJobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "clean");

            migrationBuilder.AddColumn<bool>(
                name: "SwapExplicitForClean",
                table: "CleanPlaylistJobs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetProvider",
                table: "CleanPlaylistJobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "spotify");

            // Every pre-copy job was same-service: backfill the target from the source
            // provider rather than assuming spotify.
            migrationBuilder.Sql("UPDATE \"CleanPlaylistJobs\" SET \"TargetProvider\" = \"Provider\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Isrc",
                table: "TrackMappings");

            migrationBuilder.DropColumn(
                name: "MatchMethod",
                table: "TrackMappings");

            migrationBuilder.DropColumn(
                name: "JobType",
                table: "CleanPlaylistJobs");

            migrationBuilder.DropColumn(
                name: "SwapExplicitForClean",
                table: "CleanPlaylistJobs");

            migrationBuilder.DropColumn(
                name: "TargetProvider",
                table: "CleanPlaylistJobs");
        }
    }
}
