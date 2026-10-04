using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Repairs <c>products.RatingAverage</c> / <c>products.RatingCount</c> from the published
    /// reviews that actually exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The recalculator used to read the published review set with a LINQ query issued inside the
    /// same unit of work as the review mutation. A LINQ query goes to PostgreSQL and cannot see the
    /// change tracker, so it saw the rows <em>before</em> the change it was about to be saved
    /// with. Every moderation, edit and delete therefore persisted an aggregate describing the
    /// previous state, and the drift was permanent.
    /// </para>
    /// <para>
    /// Two distinct symptoms resulted, and both survive in the database until this runs. Publishing
    /// a review left the count too low, because the review being published was not yet visible to
    /// the query; unpublishing or deleting one left it too high, because the row leaving the
    /// published set was still visible to it. Observed in production as a product listing
    /// <c>ratingCount: 1</c> while its own reviews endpoint reported two published reviews.
    /// </para>
    /// <para>
    /// This is a data repair, not a schema change, so <c>Up</c> is the whole migration and
    /// <c>Down</c> is intentionally empty. There is no correct previous value to restore: the
    /// stored numbers were derived, never authored, and recomputing them is the definition of
    /// correct. Reverting would reinstate a wrong value. The write path is fixed separately in
    /// <c>ProductReviewRatingRecalculator</c>; without that fix this migration would simply be
    /// undone by the next review.
    /// </para>
    /// <para>
    /// Safe to run repeatedly — it recomputes from <c>product_reviews</c> every time and is
    /// idempotent. It touches only products whose stored summary differs from the published set, so
    /// a database with no drift updates zero rows.
    /// </para>
    /// </remarks>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261004120000_BackfillProductRatingAggregates")]
    partial class BackfillProductRatingAggregates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Products that have at least one published review: take the count and average from
            // the published set.
            //
            // SUM() over an integer column returns bigint, so `total / cnt` is integer division and
            // truncates — 9 / 2 would be stored as 4.00 instead of 4.50, silently reproducing the
            // kind of drift this migration exists to remove. The cast to numeric has to happen
            // before the division, not after.
            //
            // ROUND on numeric rounds half away from zero, which is what
            // ReviewRatingAggregate.FromRatings does — the two must agree or a rating rewritten by
            // this migration would differ in the last decimal from one written by the app.
            migrationBuilder.Sql(
                """
                UPDATE products p
                SET "RatingCount"  = agg.cnt,
                    "RatingAverage" = ROUND(agg.total::numeric / agg.cnt::numeric, 2),
                    "UpdatedAtUtc"  = NOW()
                FROM (
                    SELECT "ProductId",
                           COUNT(*)               AS cnt,
                           SUM("Rating")::numeric AS total
                    FROM product_reviews
                    WHERE "Status" = 'Published'
                    GROUP BY "ProductId"
                ) agg
                WHERE p."Id" = agg."ProductId"
                  AND (p."RatingCount" <> agg.cnt
                       OR p."RatingAverage" <> ROUND(agg.total::numeric / agg.cnt::numeric, 2));
                """);

            // Products left with no published review at all. The first statement cannot reach them
            // because there is no aggregate row to join to, yet they are exactly where an
            // unpublish or a delete leaves a count that should have fallen to zero.
            migrationBuilder.Sql(
                """
                UPDATE products p
                SET "RatingCount"  = 0,
                    "RatingAverage" = 0,
                    "UpdatedAtUtc"  = NOW()
                WHERE (p."RatingCount" <> 0 OR p."RatingAverage" <> 0)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM product_reviews r
                      WHERE r."ProductId" = p."Id"
                        AND r."Status" = 'Published'
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see the remarks on the class.
        }
    }
}
