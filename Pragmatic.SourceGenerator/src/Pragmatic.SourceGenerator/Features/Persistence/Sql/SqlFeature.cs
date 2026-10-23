using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql;

/// <summary>
///     The Sql persistence engine's generation: every query chain on <c>SqlQuery&lt;T&gt;</c> translated at compile time into a plan its call
///     site is intercepted to run, the entity maps the runtime reads rows with, the declared queries, and Pragmatic's contracts on the engine.
///     Active in a compilation that references <c>Pragmatic.Persistence.Sql</c>; the translation is <see cref="SqlInterceptorGenerator" />.
/// </summary>
internal static class SqlFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context) => SqlInterceptorGenerator.Register(context);
}
