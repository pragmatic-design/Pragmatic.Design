using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Catalog.Entities;

/// <summary>
///     The entity that lives across the boundary.
/// </summary>
/// <remarks>
///     It belongs to <c>CatalogBoundary</c>, which by design is the only one that can write it. Sales
///     declares it with <c>[ReadAccess&lt;CatalogItem&gt;]</c> and gets a <c>DbSet</c> for it in its own
///     <c>DbContext</c>, with <c>ExcludeFromMigrations()</c> because the owner creates the table.
/// </remarks>
[Entity]
public partial class CatalogItem : IEntity
{
    [Required]
    public string Name { get; private set; } = "";

    /// <summary>
    ///     The list price, in the column the previous version of the schema called <c>Price</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[RenamedFrom]</c> is what makes the difference between a migration that <b>renames</b>
    ///         the column and one that adds a new one and drops the old: without it, the diff sees one
    ///         column more and one less, and the second takes its data with it.
    ///     </para>
    ///     <para>
    ///         ⚠️ On an empty database nothing changes — the diff looks for the old column and does not
    ///         find it — so the declaration is inert until a database of the previous version exists. It
    ///         is exactly the setup in which an attribute seems to work for years:
    ///         <c>TheRenamedColumn</c> builds that previous version and measures the rename.
    ///     </para>
    /// </remarks>
    [RenamedFrom("Price")]
    public decimal ListPrice { get; private set; }
}
