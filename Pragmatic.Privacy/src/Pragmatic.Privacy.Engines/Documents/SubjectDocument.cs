using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Privacy.Engines.Documents;

/// <summary>
///     A subject of the registry on the documents engine (collection <c>__Subjects</c>): <see cref="Sql.SubjectRow" />'s fields but the blind
///     index, which <see cref="SubjectLookupDocument" /> holds, so that its uniqueness is a document id's (see there).
/// </summary>
[Document("__Subjects")]
public sealed partial class SubjectDocument
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("__Subjects", "/id", [], []);

    /// <summary>The opaque reference. Random, never derived from the identity.</summary>
    [Key]
    public string SubjectRef { get; set; } = "";

    public string SubjectType { get; set; } = "";

    /// <summary>The encrypted identity in Base64 (the documents engine stores no byte array); null once the subject is forgotten.</summary>
    public string? Identifier { get; set; }

    /// <summary>When the reference was allocated, in UTC ticks.</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the link was broken, in UTC ticks; null while the subject is known.</summary>
    public long? ForgottenAt { get; set; }
}
