using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>The schema of one boundary's Pragmatic entities on the documents engine (<c>{Boundary}DocSchema</c>, <c>DefaultDocSchema</c> for none).</summary>
/// <param name="Hint">The generated file's hint name.</param>
/// <param name="Namespace">The class's namespace; null in the global one.</param>
/// <param name="ClassName">The class's name.</param>
/// <param name="Owner">Whose collections they are, for the class's summary: the boundary's name, or the entities of no boundary.</param>
/// <param name="Collections">Each collection's <c>new DocCollectionSchema(…)</c>, in the collections' order.</param>
internal sealed record DocSchemaModel(string Hint, string? Namespace, string ClassName, string Owner, EquatableArray<string> Collections);
