using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerWishlistAndProductReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RatingAverage",
                table: "products",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "product_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsVerifiedPurchase = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModerationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HelpfulCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_reviews", x => x.Id);
                    table.CheckConstraint("ck_product_reviews_rating", "\"Rating\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_product_reviews_product_variants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_reviews_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_reviews_users_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wishlist_items_product_variants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_wishlist_items_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wishlist_items_users_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_helpful_votes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_helpful_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_helpful_votes_product_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "product_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_review_helpful_votes_users_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_public_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    asset_secure_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    asset_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    asset_width = table.Column<int>(type: "integer", nullable: true),
                    asset_height = table.Column<int>(type: "integer", nullable: true),
                    asset_alt_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_images_product_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "product_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // -----------------------------------------------------------------------
            // NULLS NOT DISTINCT unique indexes — created with raw SQL because EF Core
            // cannot express the clause.
            //
            // Both indexes span a nullable ProductVariantId. A plain unique index treats NULL
            // as distinct, so one customer could post unlimited product-level reviews with
            // ProductVariantId = NULL, or save the same product to their wishlist any number
            // of times, while the index still looked correct in review.
            //
            // EF's model snapshot records these as ordinary unique indexes (it has no way to
            // represent the clause), which is intentional: the snapshot then matches the
            // database and EF will not try to drop and recreate them, losing the semantics.
            // Same approach already used for ix_cart_items_cart_product_variant and
            // ix_inventory_items_product_variant.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_product_reviews_customer_product_variant
                ON product_reviews (""CustomerId"", ""ProductId"", ""ProductVariantId"")
                NULLS NOT DISTINCT;
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_wishlist_items_customer_product_variant
                ON wishlist_items (""CustomerId"", ""ProductId"", ""ProductVariantId"")
                NULLS NOT DISTINCT;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_product_status_published",
                table: "product_reviews",
                columns: new[] { "ProductId", "Status", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_product_reviews_ProductVariantId",
                table: "product_reviews",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_status_created",
                table: "product_reviews",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_review_helpful_votes_CustomerId",
                table: "review_helpful_votes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "ix_review_helpful_votes_review_customer",
                table: "review_helpful_votes",
                columns: new[] { "ReviewId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_images_review_sort",
                table: "review_images",
                columns: new[] { "ReviewId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_items_customer_created",
                table: "wishlist_items",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            // Already created above with NULLS NOT DISTINCT via raw SQL. Emitting the generated
            // CreateIndex here as well would raise "relation already exists".
            migrationBuilder.CreateIndex(
                name: "ix_wishlist_items_product",
                table: "wishlist_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_items_ProductVariantId",
                table: "wishlist_items",
                column: "ProductVariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_helpful_votes");

            migrationBuilder.DropTable(
                name: "review_images");

            migrationBuilder.DropTable(
                name: "wishlist_items");

            migrationBuilder.DropTable(
                name: "product_reviews");

            migrationBuilder.DropColumn(
                name: "RatingAverage",
                table: "products");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "products");
        }
    }
}
