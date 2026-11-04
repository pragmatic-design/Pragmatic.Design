using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The queries derived from specifications, as the engines bind them; empty when EF Core is referenced or no engine is.
/// </summary>
/// <remarks>
///     An engine declares the <c>IQuery</c> shape of a declared query it sees (<c>PragmaticQuery</c>). A derived type is this generator's
///     output, which the compilation the engine is handed does not have: without its source the invoker's call to <c>RunAsync</c> had no
///     <c>IQuery</c> to infer from (CS0411, #511). The same text <see cref="DerivedQueryTemplate" /> emits, so the type the engine reads is the
///     type that compiles.
/// </remarks>
internal static class DerivedQueryEngineSources
{
    public static EquatableArray<ShapeSourceModel> For(ImmutableArray<DerivedQueryModel> models, DetectedFeatures features, EquatableArray<string> colliding)
    {
        if (features.HasPersistenceEFCore || !(features.HasPersistenceSql || features.HasPersistenceDocuments))
            return EquatableArray<ShapeSourceModel>.Empty;

        var sources = new List<ShapeSourceModel>();
        foreach (var model in models)
        {
            // A colliding name is not emitted (PRAG0726), so it is not bound either.
            if (colliding.Contains($"{model.Namespace}.{model.TypeName}"))
                continue;
            var artifact = new DerivedQueryTemplate(model).RenderOutput();
            if (!artifact.IsEmpty)
                sources.Add(new ShapeSourceModel(artifact.HintName, artifact.Text));
        }

        return sources.ToEquatableArray();
    }
}
