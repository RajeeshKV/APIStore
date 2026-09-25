namespace KromicCommerce.Contracts.Common;

/// <summary>
/// Standard paginated response wrapper.
/// Page is 1-indexed. Server enforces MaxPageSize — clients cannot request unlimited data.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
