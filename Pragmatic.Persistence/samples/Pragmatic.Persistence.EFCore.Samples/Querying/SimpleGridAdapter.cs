using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Adapters;

namespace Pragmatic.Persistence.EFCore.Samples.Querying;

/// <summary>
///     Adapter that maps an <see cref="ExternalGridRequest"/> into Pragmatic's canonical
///     <see cref="GridFilterRequest"/>. Implementing <see cref="IGridFilterAdapter{TExternalFormat}"/>
///     is pure mapping — every external grid framework plugs in the same way, decoupling the UI
///     wire format from query execution.
/// </summary>
public sealed class SimpleGridAdapter : IGridFilterAdapter<ExternalGridRequest>
{
    public GridFilterRequest Adapt(ExternalGridRequest input)
    {
        var filters = new List<FilterClause>();
        if (!string.IsNullOrWhiteSpace(input.SearchText))
            filters.Add(new FilterClause("Name", FilterOperator.Contains, input.SearchText));

        var sorts = new List<SortClause>();
        if (input.SortByPriceDesc)
            sorts.Add(new SortClause("Price", SortDirection.Descending));

        return new GridFilterRequest
        {
            Filters = filters,
            Sorts = sorts,
            // Page is 1-based; guard Take == 0 to avoid divide-by-zero.
            Page = input.Take == 0 ? 1 : input.Skip / input.Take + 1,
            PageSize = input.Take == 0 ? null : input.Take
        };
    }
}
