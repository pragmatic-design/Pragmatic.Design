using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Documents.Models;

/// <summary>A type a template reads, and the properties the generated accessor answers for.</summary>
/// <param name="FullyQualifiedName">The name the generated code writes, with global::.</param>
/// <param name="Properties">Its public instance properties with a getter, the most derived first.</param>
internal sealed record TemplateDataTypeModel(string FullyQualifiedName, EquatableArray<string> Properties);
