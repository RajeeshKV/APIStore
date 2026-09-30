using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderItemInventoryLifecycle : Migration
    {
        /// <summary>
        /// Adds the per-order-line inventory lifecycle record.
        ///
        /// BACKFILL DECISION — existing rows are deliberately left 'Untracked'.
        ///
        /// Rows that predate this migration have no trustworthy lifecycle history. The only
        /// information available about them is the CURRENT content of inventory_items, and
        /// inferring what a past order consumed from current OnHand/Reserved is precisely the
        /// unsound inference this schema exists to eliminate: other orders may have reserved
        /// units since, and stock may have been restocked. Backfilling from those counters
        /// could mark a line 'Reserved' or 'Finalized' when its units were in fact already
        /// returned, which would let a later cancellation restore stock twice.
        ///
        /// 'Untracked' is the safe direction: such a line is skipped by confirmation and by
        /// cancellation, so no stock is ever wrongly moved. The operational cost is that
        /// orders placed BEFORE this deployment do not get automatic inventory restoration on
        /// cancellation and must be handled manually. That trade favours never over-crediting
        /// stock over automating a rare migration-time case.
        ///
        /// Orders placed after the migration record their lifecycle correctly from the start.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InventoryQuantity",
                table: "order_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InventoryStatus",
                table: "order_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Untracked");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "order_items",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InventoryQuantity",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "InventoryStatus",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "order_items");
        }
    }
}
