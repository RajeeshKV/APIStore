using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7_Promotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliedCouponCode",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CodFee",
                table: "orders",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "tax_enabled",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "tax_is_price_inclusive",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tax_label",
                table: "business_settings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Tax");

            migrationBuilder.AddColumn<decimal>(
                name: "tax_percentage",
                table: "business_settings",
                type: "numeric(6,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CouponCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    MaxDiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    MinimumOrderAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    UsageLimit = table.Column<int>(type: "integer", nullable: true),
                    PerCustomerUsageLimit = table.Column<int>(type: "integer", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Applicability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsFirstOrderOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "promotion_categories",
                columns: table => new
                {
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_categories", x => new { x.PromotionId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_promotion_categories_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_products",
                columns: table => new
                {
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_products", x => new { x.PromotionId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_promotion_products_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_usages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_usages_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_categories_promotion",
                table: "promotion_categories",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_products_promotion",
                table: "promotion_products",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_usages_customer",
                table: "promotion_usages",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_usages_promotion_customer",
                table: "promotion_usages",
                columns: new[] { "PromotionId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_usages_promotion_order",
                table: "promotion_usages",
                columns: new[] { "PromotionId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotions_active",
                table: "promotions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_coupon_code",
                table: "promotions",
                column: "CouponCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotions_expires",
                table: "promotions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promotion_categories");

            migrationBuilder.DropTable(
                name: "promotion_products");

            migrationBuilder.DropTable(
                name: "promotion_usages");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropColumn(
                name: "AppliedCouponCode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CodFee",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tax_enabled",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "tax_is_price_inclusive",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "tax_label",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "tax_percentage",
                table: "business_settings");
        }
    }
}
