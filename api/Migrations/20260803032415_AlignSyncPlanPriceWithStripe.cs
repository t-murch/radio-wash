using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
    /// <summary>
    /// Corrects the seeded Sync Plan price to match what Stripe actually bills.
    /// </summary>
    /// <remarks>
    /// The Stripe price referenced by Stripe:PricePlanId charges $5.00/month, but the seeder
    /// wrote 299. Checkout bills from Stripe, so the stored value never affected the amount
    /// charged — it only fed SubscriptionController, which divides it by 100 and returns it to
    /// the client. Databases seeded before this ran therefore advertise $2.99 while charging
    /// $5.00. The seeder only populates an empty table, so it cannot repair those rows.
    ///
    /// Guarded on the old value so it corrects stale rows without overwriting a price set
    /// deliberately by some later change.
    /// </remarks>
    public partial class AlignSyncPlanPriceWithStripe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"PriceInCents\" = 500, \"UpdatedAt\" = NOW() " +
                "WHERE \"Name\" = 'Sync Plan' AND \"PriceInCents\" = 299;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"PriceInCents\" = 299, \"UpdatedAt\" = NOW() " +
                "WHERE \"Name\" = 'Sync Plan' AND \"PriceInCents\" = 500;");
        }
    }
}
