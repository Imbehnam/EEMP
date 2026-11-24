using EEMP.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace EEMP.Infrastructure.Web;

/// <summary>
/// Standard list query: page (1-based), pageSize (max 100), search. Names are pinned to the contract's camelCase so
/// the OpenAPI document advertises `page`/`pageSize`/`search` (binding itself is case-insensitive).
/// </summary>
public sealed record PagingQuery(
    [property: FromQuery(Name = "page")] int? Page,
    [property: FromQuery(Name = "pageSize")] int? PageSize,
    [property: FromQuery(Name = "search")] string? Search)
{
    public PageRequest ToPageRequest() => PageRequest.Normalize(Page, PageSize, Search);
}
