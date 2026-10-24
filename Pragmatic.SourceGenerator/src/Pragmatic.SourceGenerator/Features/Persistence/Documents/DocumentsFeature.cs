using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents;

/// <summary>
///     The documents persistence engine's generation: every [Document] and [Embedded] type's reader and writer, and every query chain on
///     <c>DocQuery&lt;T&gt;</c> translated at compile time into a plan its call site is intercepted to run. Active in a compilation that
///     references <c>Pragmatic.Persistence.Documents</c>; the translation is <see cref="DocumentGenerator" />.
/// </summary>
internal static class DocumentsFeature
{
    /// <param name="context">The generator's context.</param>
    /// <param name="shapeSources">The entities' shape files as Persistence emits them: the members a chain over a Pragmatic entity is bound against.</param>
    public static void Register(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<EquatableArray<ShapeSourceModel>> shapeSources)
        => DocumentGenerator.Register(context, shapeSources);
}
