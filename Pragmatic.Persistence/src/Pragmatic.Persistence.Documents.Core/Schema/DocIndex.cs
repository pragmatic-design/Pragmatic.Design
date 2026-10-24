namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>
///     An index a collection declares: its name, its fields in order (the stored names), whether it is unique, and the field a unique one is
///     limited to the live documents by (a soft delete's <c>IsDeleted</c>: the index holds the documents where it is false), or null.
/// </summary>
public sealed record DocIndex(string Name, IReadOnlyList<string> Fields, bool Unique = false, string? LiveField = null);
