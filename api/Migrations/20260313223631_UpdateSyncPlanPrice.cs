using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSyncPlanPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"PriceInCents\" = 500, \"UpdatedAt\" = NOW() WHERE \"Name\" = 'Sync Plan' AND \"PriceInCents\" = 299");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"PriceInCents\" = 299, \"UpdatedAt\" = NOW() WHERE \"Name\" = 'Sync Plan' AND \"PriceInCents\" = 500");
        }
    }
}
