using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
  /// <inheritdoc />
  public partial class RenameSpotifyIdToSupabaseId : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.RenameColumn(
          name: "SpotifyId",
          table: "Users",
          newName: "SupabaseId");

      migrationBuilder.RenameIndex(
          name: "IX_Users_SpotifyId",
          table: "Users",
          newName: "IX_Users_SupabaseId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.RenameColumn(
          name: "SupabaseId",
          table: "Users",
          newName: "SpotifyId");

      migrationBuilder.RenameIndex(
          name: "IX_Users_SupabaseId",
          table: "Users",
          newName: "IX_Users_SpotifyId");
    }
  }
}
