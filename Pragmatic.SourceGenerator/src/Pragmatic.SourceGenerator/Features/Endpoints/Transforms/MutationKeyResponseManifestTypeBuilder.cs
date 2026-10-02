using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Manifest.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Describes, for the API manifest, the records a <c>ReturnType = Id | LogicalKey</c> mutation
///     answers with.
/// </summary>
/// <remarks>
///     The manifest describes a type by resolving its symbol, and these records are written by this
///     generator, so the compilation it resolves against does not have them: without a description the
///     published document names a response nobody defines. The shape comes from the same model the
///     templates render, so the two cannot drift — the rule <c>PatchManifestTypeBuilder</c> follows.
/// </remarks>
internal static class MutationKeyResponseManifestTypeBuilder
{
    public static ImmutableArray<ManifestTypeModel> Build(ImmutableArray<EndpointModel> endpoints)
    {
        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint.MutationKeyResponseType is not { } type || endpoint.MutationKeyResponseProperties.Length == 0)
                continue;

            var properties = endpoint.MutationKeyResponseProperties.AsImmutableArray()
                .Select(p => new ManifestPropertyModel
                {
                    Name = p.Name,
                    Type = p.TypeFullName.Replace("global::", "").TrimEnd('?'),
                    IsNullable = p.TypeFullName.EndsWith("?", System.StringComparison.Ordinal),
                    IsRequired = true
                })
                .ToImmutableArray();

            builder.Add(new ManifestTypeModel
            {
                Type = type,
                // Every Id-returning mutation has an IdResponse: the schema is named after the mutation
                // too, or a generated client would get IdResponse, IdResponse2, … for different routes.
                SimpleName = endpoint.TypeName + type.Substring(type.LastIndexOf('.') + 1),
                Kind = ManifestTypeKind.Dto,
                Properties = new EquatableArray<ManifestPropertyModel>(properties)
            });
        }

        return builder.ToImmutable();
    }
}
