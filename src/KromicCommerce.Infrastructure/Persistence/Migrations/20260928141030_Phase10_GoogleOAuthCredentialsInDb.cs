using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_GoogleOAuthCredentialsInDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Google OAuth credential columns to business_settings.
            // All nullable — existing rows remain valid without credentials configured.
            migrationBuilder.AddColumn<string>(
                name: "auth_google_client_id",
                table: "business_settings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "auth_google_client_secret_enc",
                table: "business_settings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "auth_google_redirect_uri",
                table: "business_settings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            // The cart_items unique index was created in Phase9 with NULLS NOT DISTINCT via raw SQL.
            // EF Core's model snapshot records it as a standard unique index (cannot represent the
            // NULLS NOT DISTINCT clause). We must not let EF drop and recreate it as a standard index
            // because that would break the NULL VariantId uniqueness semantics.
            // The index already exists in the correct form — no action needed here.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_google_client_id",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_google_client_secret_enc",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "auth_google_redirect_uri",
                table: "business_settings");
        }
    }
}
