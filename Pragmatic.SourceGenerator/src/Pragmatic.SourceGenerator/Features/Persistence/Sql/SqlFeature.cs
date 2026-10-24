using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql;

/// <summary>
///     The Sql persistence engine's generation: every query chain on <c>SqlQuery&lt;T&gt;</c> translated at compile time into a plan its call
///     site is intercepted to run, the entity maps the runtime reads rows with, the declared queries, and Pragmatic's contracts on the engine.
///     Active in a compilation that references <c>Pragmatic.Persistence.Sql</c>; the translation is <see cref="SqlInterceptorGenerator" />.
/// </summary>
internal static class SqlFeature
{
    /// <param name="context">The generator's context.</param>
    /// <param name="shapeSources">The entities' shape files as Persistence emits them: the members the chains are bound against.</param>
    /// <param name="features">What the compilation references: a host's schema is typed for its EF Core provider when there is one.</param>
    /// <returns>The tables of Pragmatic's entities the engine serves, per database, for <see cref="SchemaFeature" />.</returns>
    public static IncrementalValueProvider<EquatableArray<EngineSchemaModel>> Register(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<ShapeSourceModel>> shapeSources, IncrementalValueProvider<DetectedFeatures> features)
        => SqlInterceptorGenerator.Register(context, shapeSources, features);
}
