using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enforces that a product's stock lives in exactly one place: either a single base-product row
    /// (a product with no variants) or one row per variant, never both.
    ///
    /// The mixed state reports stock the warehouse does not have. Availability is summed across a
    /// product's inventory rows, so a leftover base row sitting alongside per-variant rows
    /// double-counts and the storefront offers units that cannot be shipped.
    ///
    /// This cannot be a CHECK constraint. SQL CHECK is evaluated per row and may not reference other
    /// rows, so it cannot see whether a product already has variants. A statement-level constraint
    /// trigger with transition tables is the declarative way to express a rule about sibling rows.
    ///
    /// The check runs after the statement, so a multi-row insert or a single SaveChanges that writes
    /// both kinds of row together is caught just the same.
    /// </summary>
    /// EF discovers migrations by matching DbContextAttribute against the context, so this
    /// attribute is load-bearing: without it the migration compiles, builds and looks correct, but
    /// EF silently omits it and the constraint is never applied to any database.
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute(typeof(AppDbContext))]
    [Migration("20261002130000_InventoryRowKindConsistency")]
    public partial class InventoryRowKindConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION inventory_row_kind_consistency()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $fn$
                DECLARE
                    offending_product_id uuid;
                    affected text;
                BEGIN
                    -- Which transition tables this event actually populated. PostgreSQL forbids
                    -- naming transition tables for events that do not produce them, so the query
                    -- is built dynamically and the statement trigger only declares the tables
                    -- valid for its own event.
                    affected := CASE TG_OP
                        WHEN 'INSERT' THEN 'SELECT "ProductId" FROM new_rows'
                        WHEN 'DELETE' THEN 'SELECT "ProductId" FROM old_rows'
                        ELSE 'SELECT "ProductId" FROM new_rows
                              UNION
                              SELECT "ProductId" FROM old_rows'
                    END;

                    -- Only the products this statement actually touched, so the check stays cheap
                    -- on bulk inventory writes instead of scanning the whole table.
                    EXECUTE format($q$
                        SELECT t."ProductId"
                        FROM (%s) AS t
                        WHERE (
                                  SELECT count(*) FROM inventory_items i
                                  WHERE i."ProductId" = t."ProductId"
                                    AND i."VariantId" IS NULL
                              ) > 0
                          AND (
                                  SELECT count(*) FROM inventory_items i
                                  WHERE i."ProductId" = t."ProductId"
                                    AND i."VariantId" IS NOT NULL
                              ) > 0
                        LIMIT 1
                    $q$, affected)
                    INTO offending_product_id;

                    IF offending_product_id IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Inventory for product % mixes a base-product row with per-variant rows. A product must have either a single base row or one row per variant, never both, because availability is summed across rows and a mixed set overstates stock.',
                            offending_product_id
                            USING ERRCODE = '23514';
                    END IF;

                    RETURN NULL;
                END;
                $fn$;
                """);

            // One trigger per event, because PostgreSQL rejects transition tables on a trigger
            // declared with more than one event ("transition tables cannot be specified for
            // triggers with more than one event"). Each declares only the tables its event fills.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER ck_inventory_row_kind_consistency_insert
                AFTER INSERT ON inventory_items
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT
                EXECUTE FUNCTION inventory_row_kind_consistency();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER ck_inventory_row_kind_consistency_update
                AFTER UPDATE ON inventory_items
                REFERENCING NEW TABLE AS new_rows OLD TABLE AS old_rows
                FOR EACH STATEMENT
                EXECUTE FUNCTION inventory_row_kind_consistency();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER ck_inventory_row_kind_consistency_delete
                AFTER DELETE ON inventory_items
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT
                EXECUTE FUNCTION inventory_row_kind_consistency();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ck_inventory_row_kind_consistency_insert ON inventory_items;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ck_inventory_row_kind_consistency_update ON inventory_items;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ck_inventory_row_kind_consistency_delete ON inventory_items;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory_row_kind_consistency();");
        }
    }
}
