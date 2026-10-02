namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Moderation state of a customer product review.
///
/// Only <see cref="Published"/> reviews are visible to the public list, contribute to the
/// product rating aggregate, or can be sorted by rating/helpful. A <see cref="Pending"/>
/// review is visible to its author and to admins, and nowhere else.
/// </summary>
public enum ReviewStatus
{
    Pending = 0,
    Published = 1,
    Rejected = 2
}