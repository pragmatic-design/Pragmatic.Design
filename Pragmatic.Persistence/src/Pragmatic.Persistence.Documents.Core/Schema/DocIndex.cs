namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>
///     An index a collection declares: its name, its fields in order (the stored names), whether it is unique, and what a unique one is limited
///     to. <paramref name="LiveField" />: the live documents (a soft delete's <c>IsDeleted</c>: the index holds the documents where it is false).
///     <paramref name="NullField" />: the documents where the field is null (a temporal relation's <c>ValidTo</c>: the open stretches, #900).
///     Both null: every document.
/// </summary>
public sealed record DocIndex(string Name, IReadOnlyList<string> Fields, bool Unique = false, string? LiveField = null, string? NullField = null);
