using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
  /// <inheritdoc />
  public partial class ConvertStatusColumnType : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AlterColumn<string>(
          name: "Status",
          table: "CleanPlaylistJobs",
          type: "text",
          nullable: false,
          oldClrType: typeof(int),
          oldType: "integer");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AlterColumn<int>(
          name: "Status",
          table: "CleanPlaylistJobs",
          type: "integer",
          nullable: false,
          oldClrType: typeof(string),
          oldType: "text");
    }
  }
}
