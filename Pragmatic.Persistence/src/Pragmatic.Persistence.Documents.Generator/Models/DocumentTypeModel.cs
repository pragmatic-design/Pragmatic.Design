using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Documents.Generator.Models;

/// <summary>
///     A [Document] or [Embedded] partial class as its generated half implements it: the contract, the fields in declaration order, and for a
///     document its collection, key, version and partition key.
/// </summary>
/// <param name="Hint">The generated file's hint name.</param>
/// <param name="Namespace">The type's namespace; null in the global one.</param>
/// <param name="TypeName">The type's simple name.</param>
/// <param name="FullName">The type's fully qualified name.</param>
/// <param name="Contract"><c>IDocumentEntity&lt;T&gt;</c> or <c>IEmbeddedDocument&lt;T&gt;</c>, implemented explicitly.</param>
/// <param name="IsEmbedded">An [Embedded] type: no collection, key, version or partition key.</param>
/// <param name="CollectionLiteral">A document's collection, as a C# string literal.</param>
/// <param name="Fields">The fields in declaration order.</param>
/// <param name="KeyWrite">The expression that writes a document's key under the name <c>name</c>.</param>
/// <param name="Versioned">A document with a version field.</param>
/// <param name="VersionGet">The expression that reads a document's version as a long.</param>
/// <param name="VersionSet">The statement that sets a document's version from <c>version</c>; empty without one.</param>
/// <param name="IdText">The expression that is a document's key as Cosmos DB's <c>id</c>.</param>
/// <param name="PartitionKeyPathLiteral">A document's partition key path, as a C# string literal.</param>
/// <param name="PartitionKeyWrite">The expression that writes a document's partition key value.</param>
/// <param name="Families">A Pragmatic entity's filter families, as their initializers; empty otherwise.</param>
internal sealed record DocumentTypeModel(
    string Hint,
    string? Namespace,
    string TypeName,
    string FullName,
    string Contract,
    bool IsEmbedded,
    string? CollectionLiteral,
    EquatableArray<DocumentFieldModel> Fields,
    string? KeyWrite,
    bool Versioned,
    string VersionGet,
    string VersionSet,
    string? IdText,
    string? PartitionKeyPathLiteral,
    string? PartitionKeyWrite,
    EquatableArray<string> Families);
