namespace EEMP.SharedKernel.Paging;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record PageRequest(int Page, int PageSize, string? Search)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public static PageRequest Normalize(int? page, int? pageSize, string? search) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize),
            string.IsNullOrWhiteSpace(search) ? null : search.Trim());

    public int Skip => (Page - 1) * PageSize;
}
