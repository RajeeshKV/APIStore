using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantIdToProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "product_images",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_images_variant_sort",
                table: "product_images",
                columns: new[] { "ProductId", "VariantId", "SortOrder" },
                filter: "\"VariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_product_images_VariantId",
                table: "product_images",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_product_images_product_variants_VariantId",
                table: "product_images",
                column: "VariantId",
                principalTable: "product_variants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_images_product_variants_VariantId",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "ix_product_images_variant_sort",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "IX_product_images_VariantId",
                table: "product_images");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "product_images");
        }
    }
}
