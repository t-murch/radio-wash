using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnhancedUserMusicTokenSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Scopes",
                table: "UserMusicTokens",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderMetadata",
                table: "UserMusicTokens",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "UserMusicTokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshAt",
                table: "UserMusicTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefreshFailureCount",
                table: "UserMusicTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRevoked",
                table: "UserMusicTokens");

            migrationBuilder.DropColumn(
                name: "LastRefreshAt",
                table: "UserMusicTokens");

            migrationBuilder.DropColumn(
                name: "RefreshFailureCount",
                table: "UserMusicTokens");

            migrationBuilder.AlterColumn<string>(
                name: "Scopes",
                table: "UserMusicTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderMetadata",
                table: "UserMusicTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
