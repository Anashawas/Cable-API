namespace Application.Common.Models;

/// <summary>
/// Standard paged response shape for admin list endpoints:
/// { items, totalCount, page, pageSize, totalPages, hasNextPage, hasPreviousPage }.
/// The pager-state fields are computed, so every construction gets them for free.
/// </summary>
public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1 && TotalCount > 0;

    /// <summary>Re-wraps the envelope around transformed items (e.g. entity → DTO) keeping the paging metadata.</summary>
    public PagedResult<TOut> As<TOut>(List<TOut> items) => new(items, TotalCount, Page, PageSize);
}
