using EEMP.SharedKernel.Paging;

namespace EEMP.Infrastructure.Web;

/// <summary>Standard list query: page (1-based), pageSize (max 100), search.</summary>
public sealed record PagingQuery(int? Page, int? PageSize, string? Search)
{
    public PageRequest ToPageRequest() => PageRequest.Normalize(Page, PageSize, Search);
}
