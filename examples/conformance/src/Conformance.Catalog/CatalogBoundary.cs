using Pragmatic.Actions.Attributes;

namespace Conformance.Catalog;

/// <summary>
///     The second boundary. It exists to demonstrate the <em>boundary</em>, not a second domain.
/// </summary>
/// <remarks>
///     It owns <c>CatalogItem</c>. Sales reads it with <c>[ReadAccess&lt;CatalogItem&gt;]</c>: same
///     database, a table of another owner. The cells that need it are two — what <c>[ReadAccess]</c>
///     really grants, and what a write that tries to cross the boundary does.
/// </remarks>
[Boundary]
public partial class CatalogBoundary;
