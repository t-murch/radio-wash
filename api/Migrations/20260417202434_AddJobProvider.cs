using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJobProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "CleanPlaylistJobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "spotify");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "CleanPlaylistJobs");
        }
    }
}
