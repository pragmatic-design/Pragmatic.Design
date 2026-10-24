using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents;

/// <summary>
///     The documents persistence engine's generation: every [Document] and [Embedded] type's reader and writer, and every query chain on
///     <c>DocQuery&lt;T&gt;</c> translated at compile time into a plan its call site is intercepted to run. Active in a compilation that
///     references <c>Pragmatic.Persistence.Documents</c>; the translation is <see cref="DocumentGenerator" />.
/// </summary>
internal static class DocumentsFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context) => DocumentGenerator.Register(context);
}
