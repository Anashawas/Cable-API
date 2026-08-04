using Microsoft.EntityFrameworkCore;

namespace Application.Common.Models;

/// <summary>
/// One-line pagination over IQueryable (ported from the GeoAttendance pagination
/// module, adapted to Cable's PagedResult envelope). Always order the source
/// first for deterministic pages.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>Always paginates. Page defaults to 1; pageSize to <paramref name="defaultPageSize"/>, capped at <paramref name="maxPageSize"/>.</summary>
    public static async Task<PagedResult<T>> ToPaginatedAsync<T>(
        this IQueryable<T> source,
        int? page,
        int? pageSize,
        int defaultPageSize = 20,
        int maxPageSize = 500,
        CancellationToken cancellationToken = default)
    {
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? defaultPageSize, 1, maxPageSize);

        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, totalCount, p, size);
    }

    /// <summary>
    /// Opt-in pagination: when BOTH page and pageSize are omitted, ALL rows are
    /// returned as a single page (legacy behaviour preserved); otherwise paginates.
    /// </summary>
    public static async Task<PagedResult<T>> ToOptionallyPaginatedAsync<T>(
        this IQueryable<T> source,
        int? page,
        int? pageSize,
        int defaultPageSize = 20,
        int maxPageSize = 500,
        CancellationToken cancellationToken = default)
    {
        if (page.HasValue || pageSize.HasValue)
            return await source.ToPaginatedAsync(page, pageSize, defaultPageSize, maxPageSize, cancellationToken);

        var items = await source.ToListAsync(cancellationToken);
        return new PagedResult<T>(items, items.Count, 1, items.Count);
    }

    /// <summary>
    /// In-memory variant for handlers whose rows are produced by raw SQL or
    /// post-processing. Slices the already-materialized list — trims the payload,
    /// not the DB work.
    /// </summary>
    public static PagedResult<T> ToOptionallyPaginated<T>(
        this List<T> source,
        int? page,
        int? pageSize,
        int defaultPageSize = 20,
        int maxPageSize = 500)
    {
        if (!page.HasValue && !pageSize.HasValue)
            return new PagedResult<T>(source, source.Count, 1, source.Count);

        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? defaultPageSize, 1, maxPageSize);
        var items = source.Skip((p - 1) * size).Take(size).ToList();
        return new PagedResult<T>(items, source.Count, p, size);
    }
}
