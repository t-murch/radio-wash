using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripePriceIdToSubscriptionPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripePriceId",
                table: "SubscriptionPlans",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripePriceId",
                table: "SubscriptionPlans");
        }
    }
}
