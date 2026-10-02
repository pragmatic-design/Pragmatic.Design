using Conformance.Catalog.Entities;
using Pragmatic.Actions.Attributes;

namespace Conformance.Sales;

/// <summary>
///     The boundary where every relation combination lives.
/// </summary>
/// <remarks>
///     <para>
///         A nested write stays inside a boundary, so the whole relation matrix is demonstrated here.
///         <c>CatalogBoundary</c> exists for the opposite cell: the <em>boundary</em> itself.
///     </para>
///     <para>
///         ⚠️ <c>[ReadAccess&lt;CatalogItem&gt;]</c> adds a <c>DbSet&lt;CatalogItem&gt;</c> to this
///         <c>DbContext</c>, with <c>ExcludeFromMigrations()</c> because the owner creates the table.
///         The name says «read»: what it really grants is what
///         <c>ReadAccessAcrossTheBoundary</c> measures.
///     </para>
/// </remarks>
[Boundary]
[ReadAccess<CatalogItem>]
public partial class SalesBoundary;
