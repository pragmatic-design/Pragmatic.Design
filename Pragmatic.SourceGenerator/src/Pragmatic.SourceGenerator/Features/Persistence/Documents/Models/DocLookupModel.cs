namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     The related documents one navigation of a declared query's projection reads (<c>[MapProperty("Nav.Field")]</c>): a document keeps the relation
///     as its key, so the related ones are read by the keys of the page's documents, through the filters a navigation's target carries
///     (<see cref="Declared" />), and found by their key.
/// </summary>
/// <param name="Index">Its number among the case's lookups: the names of the generated locals (<c>__related{Index}</c>).</param>
/// <param name="RelatedType">The related entity, fully qualified.</param>
/// <param name="ForeignKey">The document's field holding the related one's key (<c>GuestId</c>).</param>
/// <param name="ForeignKeyNullable">Whether that field is optional: a document without one reads no related document.</param>
/// <param name="RelatedKey">The related document's key property (<c>PersistenceId</c>).</param>
/// <param name="KeyType">The key's type, fully qualified, not nullable.</param>
internal sealed record DocLookupModel(int Index, string RelatedType, string ForeignKey, bool ForeignKeyNullable, string RelatedKey, string KeyType)
{
    /// <summary>The navigation the lookup reads for (<c>Guest</c>): what a query that maps in memory sets on the document before its Selector (#505).</summary>
    public string Navigation { get; init; } = "";

    /// <summary>
    ///     An inner <c>[Join&lt;T&gt;]</c>'s lookup (#508): a document whose related one is not found (missing, or outside the caller's filters) is
    ///     dropped, as the inner join drops its row.
    /// </summary>
    public bool Drops { get; init; }

    /// <summary>
    ///     A declared <c>[Join&lt;T&gt;]</c>'s lookup: read through all the caller's filters, as the Sql engine's declared join reads its side. A
    ///     navigation's lookup is read as EF Core reads a navigation, its owner and scopes lifted (#580).
    /// </summary>
    public bool Declared { get; init; }
}
