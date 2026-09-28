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
            //   - Keep the row with the earliest AddedAt (lowest "Id" as tiebreaker).
            //   - Sum the quantities from all duplicates into the surviving row.
            //   - Delete the duplicate rows.
            //
            // NOTE: EF Core's default column naming convention for this project uses
            // PascalCase quoted identifiers (e.g. "Id", "CartId"). PostgreSQL column
            // names ARE case-sensitive when the table was created with quoted names.
            // All column references below use the exact quoted names EF generated.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql(@"
                WITH duplicates AS (
                    SELECT
                        ""Id"",
                        ""CartId"",
                        ""ProductId"",
                        ""VariantId"",
                        ""Quantity"",
                        ROW_NUMBER() OVER (
                            PARTITION BY ""CartId"", ""ProductId"", ""VariantId""
                            ORDER BY ""AddedAt"", ""Id""
                        ) AS rn,
                        SUM(""Quantity"") OVER (
                            PARTITION BY ""CartId"", ""ProductId"", ""VariantId""
                        ) AS total_qty
                    FROM cart_items
                )
                UPDATE cart_items ci
                SET ""Quantity"" = d.total_qty
                FROM duplicates d
                WHERE ci.""Id"" = d.""Id"" AND d.rn = 1;

                DELETE FROM cart_items
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT
                            ""Id"",
                            ROW_NUMBER() OVER (
                                PARTITION BY ""CartId"", ""ProductId"", ""VariantId""
                                ORDER BY ""AddedAt"", ""Id""
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
