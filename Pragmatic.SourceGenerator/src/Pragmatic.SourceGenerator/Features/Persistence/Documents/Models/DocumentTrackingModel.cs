using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     A Pragmatic entity's change detection as a document (<c>IDocumentTracked&lt;T&gt;</c>): every field but the key and the version, a bit each
///     in declaration order.
/// </summary>
/// <param name="Hint">The generated file's hint name.</param>
/// <param name="Namespace">The entity's namespace; null in the global one.</param>
/// <param name="TypeName">The entity's simple name.</param>
/// <param name="FullName">The entity's fully qualified name.</param>
/// <param name="Fields">The tracked fields, in the order of their bits.</param>
internal sealed record DocumentTrackingModel(string Hint, string? Namespace, string TypeName, string FullName, EquatableArray<TrackedFieldModel> Fields);
