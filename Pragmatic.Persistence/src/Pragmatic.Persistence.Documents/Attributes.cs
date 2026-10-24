namespace Pragmatic.Persistence.Documents;

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

/// <summary>The document's identity, stored as <c>_id</c>.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class KeyAttribute : Attribute;

/// <summary>An integer the writes check and increment: a replace or delete of a stale copy is refused (<see cref="DocConcurrencyException" />).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VersionAttribute : Attribute;

/// <summary>A type stored inside its document (a property of that type is a nested document; a list of it, an array). The class is <c>partial</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EmbeddedAttribute : Attribute;
