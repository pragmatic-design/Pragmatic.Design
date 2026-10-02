namespace Pragmatic.Persistence.EFCore.Samples.Querying;

/// <summary>
///     A minimal external grid request — stands in for a real UI grid's payload
///     (DevExpress DataSourceLoadOptions, PrimeNG LazyLoadEvent, AG Grid request, …).
/// </summary>
public sealed record ExternalGridRequest(string? SearchText, int Skip, int Take, bool SortByPriceDesc);
