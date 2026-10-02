using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.EFCore.Samples.Filtering;

/// <summary>
///     A stand-in filter type used only to key the <see cref="IQueryFilterToggle"/> in
///     <see cref="FilterToggleSample"/>. In a real app this would be the SG-generated
///     soft-delete filter for a specific entity (e.g. the nested <c>Product.SoftDeleteFilter</c>);
///     the toggle disables filters by their CLR type.
/// </summary>
public sealed class SampleSoftDeleteFilter : IQueryFilter;
