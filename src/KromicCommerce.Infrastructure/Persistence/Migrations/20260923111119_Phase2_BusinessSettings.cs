using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_BusinessSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "business_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "business_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "business_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemporaryClosureMessage",
                table: "business_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitterUrl",
                table: "business_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "business_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YoutubeUrl",
                table: "business_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "auth_email_password_enabled",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "auth_google_oauth_enabled",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "auth_mobile_otp_enabled",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "auth_otp_expiry_minutes",
                table: "business_settings",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "auth_otp_max_attempts",
                table: "business_settings",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "auth_otp_resend_cooldown_seconds",
                table: "business_settings",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "auth_sms_provider",
                table: "business_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Fast2SMS");

            migrationBuilder.AddColumn<bool>(
                name: "delivery_cod_enabled",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_cod_extra_fee",
                table: "business_settings",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_flat_fee_amount",
                table: "business_settings",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_free_shipping_threshold",
                table: "business_settings",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivery_max_days",
                table: "business_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "delivery_min_days",
                table: "business_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "delivery_processing_days",
                table: "business_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "email_mode",
                table: "business_settings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "KromicManaged");

            migrationBuilder.AddColumn<string>(
                name: "email_sender_email",
                table: "business_settings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_sender_name",
                table: "business_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Store");

            migrationBuilder.AddColumn<string>(
                name: "seo_favicon_url",
                table: "business_settings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_meta_description",
                table: "business_settings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_meta_keywords",
                table: "business_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_meta_title",
                table: "business_settings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_og_image_url",
                table: "business_settings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "TemporaryClosureMessage",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "TwitterUrl",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "YoutubeUrl",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_email_password_enabled",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_google_oauth_enabled",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_mobile_otp_enabled",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_otp_expiry_minutes",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_otp_max_attempts",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_otp_resend_cooldown_seconds",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_sms_provider",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_cod_enabled",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_cod_extra_fee",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_flat_fee_amount",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_free_shipping_threshold",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_max_days",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_min_days",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "delivery_processing_days",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "email_mode",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "email_sender_email",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "email_sender_name",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "seo_favicon_url",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "seo_meta_description",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "seo_meta_keywords",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "seo_meta_title",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "seo_og_image_url",
                table: "business_settings");
        }
    }
}
