using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Privacy.Engines.Documents;

/// <summary>
///     The blind index of a known subject on the documents engine (collection <c>__SubjectLookups</c>): its id is the index, so two live subjects
///     cannot share one, and forgetting the subject deletes it.
/// </summary>
/// <remarks>
///     ⚠️ Not a unique index on the subject: the index must be unique among the subjects that have one, and null for the forgotten. MongoDB limits
///     a partial index to the live documents of a soft delete, and Cosmos DB has none, and both count a missing value in a unique one: the second
///     forgotten subject would be refused. A document's id is unique on both, and a deleted document holds nothing.
/// </remarks>
[Document("__SubjectLookups")]
public sealed partial class SubjectLookupDocument
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("__SubjectLookups", "/id", [], []);

    /// <summary>The blind index in Base64.</summary>
    [Key]
    public string LookupIndex { get; set; } = "";

    /// <summary>The subject it finds.</summary>
    public string SubjectRef { get; set; } = "";
}
