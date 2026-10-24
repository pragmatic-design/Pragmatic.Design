using System.Text;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     A field an ordering chosen at runtime sorts by, named as each backend stores it (written by the generator, never by a caller): MongoDB's
///     path (<c>_id</c> for the key) and Cosmos DB's (<c>c["id"]</c>), and the path's segments, which a merge across partitions reads back.
/// </summary>
public sealed class DocOrderKey(string mongo, string cosmos, params string[] segments)
{
    public string Mongo { get; } = mongo;

    public string Cosmos { get; } = cosmos;

    /// <summary>The field's segments in a Cosmos DB document (<c>id</c>, not <c>_id</c>).</summary>
    public IReadOnlyList<string> Segments { get; } = segments;
}
