using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    /// <summary>
    ///     The declarations without a <c>[RequireExists]</c> a <c>[LoadEntity]</c> of the same entity and key already
    ///     proves — each reported (<c>PRAG0462</c>): a row that was read exists, and asking again is a query for nothing.
    /// </summary>
    private static ImmutableArray<LoadEntityModel> WithoutExistsALoadProves(
        ImmutableArray<LoadEntityModel>.Builder loads, ImmutableArray<LoadEntityDiagnosticInfo>.Builder diagnostics)
    {
        var kept = ImmutableArray.CreateBuilder<LoadEntityModel>(loads.Count);
        foreach (var load in loads)
        {
            var proved = load.ExistsOnly && loads.Any(other =>
                other is { ExistsOnly: false, IsMany: false, IsBySpecification: false }
                && other.EntityTypeFullName == load.EntityTypeFullName
                && other.IdPropertyName == load.IdPropertyName
                && other.LogicKeyMember is null);
            if (!proved)
            {
                kept.Add(load);
                continue;
            }

            diagnostics.Add(new LoadEntityDiagnosticInfo
            {
                EntityTypeName = load.EntityTypeShortName,
                Kind = LoadEntityDiagnosticKind.ExistsBesideALoad,
                AttributeName = "RequireExists",
                IdPropertyName = load.IdPropertyName
            });
        }

        return kept.ToImmutable();
    }
}
