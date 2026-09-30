using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VariantAttributesCartCouponPaymentRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -----------------------------------------------------------------------
            // Step 1: De-duplicate product attributes and attribute values before the
            // unique indexes below are created. The previous indexes were non-unique, so
            // concurrent admin saves could have produced duplicates. Existing values are
            // merged into the earliest row of each group; variants that referenced a
            // discarded value keep their stored ID and simply resolve to no label.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql(@"
                DELETE FROM product_attribute_values
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                            ROW_NUMBER() OVER (
                                PARTITION BY ""AttributeId"", ""Value""
                                ORDER BY ""SortOrder"", ""Id""
                            ) AS rn
                        FROM product_attribute_values
                    ) ranked
                    WHERE rn > 1
                );

                DELETE FROM product_attributes
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                            ROW_NUMBER() OVER (
                                PARTITION BY ""ProductId"", ""Name""
                                ORDER BY ""SortOrder"", ""Id""
                            ) AS rn
                        FROM product_attributes
                    ) ranked
                    WHERE rn > 1
                );
            ");

            // -----------------------------------------------------------------------
            // Step 2: Merge duplicate base-product inventory rows.
            //
            // ix_inventory_items_product_variant was a plain unique index, and PostgreSQL
            // treats NULLs as distinct, so multiple rows with VariantId = NULL could exist for
            // the same product. Stock was then read from an arbitrary one of them. Keep the
            // earliest row and sum OnHand/Reserved so no committed quantity is lost.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT ""Id"",
                        ROW_NUMBER() OVER (
                            PARTITION BY ""ProductId"", ""VariantId""
                            ORDER BY ""UpdatedAt"", ""Id""
                        ) AS rn
                    FROM inventory_items
                ), merged AS (
                    SELECT ""Id"",
                        SUM(""OnHand"") OVER (PARTITION BY ""ProductId"", ""VariantId"") AS on_hand,
                        SUM(""Reserved"") OVER (PARTITION BY ""ProductId"", ""VariantId"") AS reserved
                    FROM inventory_items
                )
                UPDATE inventory_items i
                SET ""OnHand"" = m.on_hand, ""Reserved"" = m.reserved
                FROM merged m, ranked r
                WHERE i.""Id"" = m.""Id"" AND r.""Id"" = i.""Id"" AND r.rn = 1;

                DELETE FROM inventory_items
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                            ROW_NUMBER() OVER (
                                PARTITION BY ""ProductId"", ""VariantId""
                                ORDER BY ""UpdatedAt"", ""Id""
                            ) AS rn
                        FROM inventory_items
                    ) ranked
                    WHERE rn > 1
                );
            ");

            migrationBuilder.DropIndex(
                name: "ix_product_attributes_product_name",
                table: "product_attributes");

            migrationBuilder.DropIndex(
                name: "ix_product_attribute_values_attr_value",
                table: "product_attribute_values");

            migrationBuilder.DropIndex(
                name: "ix_inventory_items_product_variant",
                table: "inventory_items");

            migrationBuilder.AddColumn<string>(
                name: "ProviderRefundId",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAtUtc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "carts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_attributes_product_name",
                table: "product_attributes",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_attr_value",
                table: "product_attribute_values",
                columns: new[] { "AttributeId", "Value" },
                unique: true);

            // NULLS NOT DISTINCT (PostgreSQL 15+) so a null VariantId is a single key.
            // EF cannot express this, so it is created with raw SQL — the same approach
            // already used for ix_cart_items_cart_product_variant.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_inventory_items_product_variant
                ON inventory_items (""ProductId"", ""VariantId"")
                NULLS NOT DISTINCT;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_refund",
                table: "payments",
                column: "ProviderRefundId",
                filter: "\"ProviderRefundId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_product_attributes_product_name",
                table: "product_attributes");

            migrationBuilder.DropIndex(
                name: "ix_product_attribute_values_attr_value",
                table: "product_attribute_values");

            migrationBuilder.DropIndex(
                name: "ix_payments_provider_refund",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_inventory_items_product_variant",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "ProviderRefundId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAtUtc",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "carts");

            migrationBuilder.CreateIndex(
                name: "ix_product_attributes_product_name",
                table: "product_attributes",
                columns: new[] { "ProductId", "Name" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_attr_value",
                table: "product_attribute_values",
                columns: new[] { "AttributeId", "Value" });

            // Restore the previous non-unique index, which tolerated duplicate NULL VariantIds.
            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_product_variant",
                table: "inventory_items",
                columns: new[] { "ProductId", "VariantId" });
        }
    }
}
