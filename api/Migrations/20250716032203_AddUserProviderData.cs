using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RadioWash.Api.Migrations
{
  /// <inheritdoc />
  public partial class AddUserProviderData : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
          name: "PrimaryProvider",
          table: "Users",
          type: "text",
          nullable: true);

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

      migrationBuilder.CreateIndex(
          name: "IX_UserProviderData_Provider_ProviderId",
          table: "UserProviderData",
          columns: new[] { "Provider", "ProviderId" },
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_UserProviderData_UserId",
          table: "UserProviderData",
          column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "UserProviderData");

      migrationBuilder.DropColumn(
          name: "PrimaryProvider",
          table: "Users");
    }
  }
}
