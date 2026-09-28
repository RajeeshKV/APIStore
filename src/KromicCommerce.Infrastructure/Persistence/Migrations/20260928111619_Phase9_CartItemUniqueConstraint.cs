using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_CartItemUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -----------------------------------------------------------------------
            // Step 1: Merge any duplicate cart_items that may exist in production.
            //
            // Duplicates can arise from the previous non-unique index when concurrent
            // AddToCart requests both found no existing item and each inserted a new row.
            //
            // Strategy:
            //   - Keep the row with the earliest AddedAt (lowest id as tiebreaker).
            //   - Sum the quantities from all duplicates into the surviving row.
            //   - Delete the duplicate rows.
            //
            // The CTE identifies groups with more than one row sharing the same
            // (CartId, ProductId, VariantId) — NULL-safe via IS NOT DISTINCT FROM.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql(@"
                WITH duplicates AS (
                    SELECT
                        id,
                        ""CartId"",
                        ""ProductId"",
                        ""VariantId"",
                        ""Quantity"",
                        ROW_NUMBER() OVER (
                            PARTITION BY ""CartId"", ""ProductId"", ""VariantId""
                            ORDER BY ""AddedAt"", id
                        ) AS rn,
                        SUM(""Quantity"") OVER (
                            PARTITION BY ""CartId"", ""ProductId"", ""VariantId""
                        ) AS total_qty
                    FROM cart_items
                )
                UPDATE cart_items ci
                SET ""Quantity"" = d.total_qty
                FROM duplicates d
                WHERE ci.id = d.id AND d.rn = 1;

                DELETE FROM cart_items
                WHERE id IN (
                    SELECT id FROM (
                        SELECT
                            id,
                            ROW_NUMBER() OVER (
                                PARTITION BY ""CartId"", ""ProductId"", ""VariantId""
                                ORDER BY ""AddedAt"", id
                            ) AS rn
                        FROM cart_items
                    ) ranked
                    WHERE rn > 1
                );
            ");

            // -----------------------------------------------------------------------
            // Step 2: Drop the old non-unique performance index.
            // -----------------------------------------------------------------------
            migrationBuilder.DropIndex(
                name: "ix_cart_items_cart_product_variant",
                table: "cart_items");

            // -----------------------------------------------------------------------
            // Step 3: Create the unique index with NULLS NOT DISTINCT so that
            // (CartId, ProductId, NULL VariantId) is treated as a single unique key.
            //
            // Standard PostgreSQL unique indexes treat NULL as distinct from every
            // other NULL, which would allow unlimited rows with VariantId = NULL for
            // the same (CartId, ProductId). NULLS NOT DISTINCT (PostgreSQL 15+) fixes
            // this so NULL equals NULL for uniqueness purposes.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_cart_items_cart_product_variant
                ON cart_items (""CartId"", ""ProductId"", ""VariantId"")
                NULLS NOT DISTINCT;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the unique index with NULLS NOT DISTINCT
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_cart_items_cart_product_variant;
            ");

            // Restore the original non-unique performance index
            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_product_variant",
                table: "cart_items",
                columns: new[] { "CartId", "ProductId", "VariantId" });
        }
    }
}
