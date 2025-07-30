using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartProgressFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatchSize",
                table: "CleanPlaylistJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentBatch",
                table: "CleanPlaylistJobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatchSize",
                table: "CleanPlaylistJobs");

            migrationBuilder.DropColumn(
                name: "CurrentBatch",
                table: "CleanPlaylistJobs");
        }
    }
}
