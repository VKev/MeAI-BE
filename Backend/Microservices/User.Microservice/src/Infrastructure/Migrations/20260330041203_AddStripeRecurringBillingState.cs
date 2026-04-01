using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeRecurringBillingState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stripe_schedule_id",
                table: "user_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_subscription_id",
                table: "user_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_reference_id",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_price_id",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_product_id",
                table: "subscriptions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stripe_schedule_id",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "stripe_subscription_id",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "provider_reference_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "stripe_price_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "stripe_product_id",
                table: "subscriptions");
        }
    }
}
