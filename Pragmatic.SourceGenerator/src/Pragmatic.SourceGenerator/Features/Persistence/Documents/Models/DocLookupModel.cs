namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     The related documents one navigation of a declared query's projection reads (<c>[MapProperty("Nav.Field")]</c>): a document keeps the relation
///     as its key, so the related ones are read by the keys of the page's documents, through the session's filters, and found by their key.
/// </summary>
/// <param name="Index">Its number among the case's lookups: the names of the generated locals (<c>__related{Index}</c>).</param>
/// <param name="RelatedType">The related entity, fully qualified.</param>
/// <param name="ForeignKey">The document's field holding the related one's key (<c>GuestId</c>).</param>
/// <param name="ForeignKeyNullable">Whether that field is optional: a document without one reads no related document.</param>
/// <param name="RelatedKey">The related document's key property (<c>PersistenceId</c>).</param>
/// <param name="KeyType">The key's type, fully qualified, not nullable.</param>
internal sealed record DocLookupModel(int Index, string RelatedType, string ForeignKey, bool ForeignKeyNullable, string RelatedKey, string KeyType);
