namespace Pragmatic.Persistence.Documents.Attributes;

/// <summary>
///     An aggregate stored as one document of <paramref name="collection" />: its scalar properties are the document's fields, an
///     <see cref="EmbeddedAttribute" /> type (or a list of them) is a nested document (an array of them), nothing refers to another
///     document. The class is <c>partial</c>: the generator adds its reader and writer (<see cref="IDocumentEntity{TSelf}" />).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DocumentAttribute(string collection) : Attribute
{
    public string Collection { get; } = collection;

    /// <summary>The property the documents are partitioned by (Cosmos DB's partition key, a shard key): the chains that name it in an equality stay in one partition.</summary>
    public string? PartitionKey { get; init; }
}
