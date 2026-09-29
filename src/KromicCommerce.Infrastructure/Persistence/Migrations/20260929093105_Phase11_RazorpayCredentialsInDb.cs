using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase11_RazorpayCredentialsInDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "payment_enabled",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_razorpay_key_id",
                table: "business_settings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_razorpay_key_secret_enc",
                table: "business_settings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_razorpay_webhook_secret_enc",
                table: "business_settings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_enabled",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "payment_razorpay_key_id",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "payment_razorpay_key_secret_enc",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "payment_razorpay_webhook_secret_enc",
                table: "business_settings");
        }
    }
}
