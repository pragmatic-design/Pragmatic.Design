using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Says that this assembly takes the request's tenant into an operation, so a host can answer for the
///     <c>ITenantContext</c> the generated invoker reads — or say it cannot (<c>PRAG1615</c>).
/// </summary>
/// <remarks>
///     ⚠️ The declaration and the implementation are in different packages, as the clock's are:
///     <c>[FromTenant]</c> and <c>ITenantContext</c> are in Abstractions, which every module has, while
///     the tenant resolution is in Pragmatic.MultiTenancy, which a module usually does not reference and a
///     host has to. So the module declares and the host decides.
/// </remarks>
internal static class TenantBindingsFeature
{
    private const string SchemaVersion = "1.0";

    /// <returns>The host-local entry, for a host that declares a <c>[FromTenant]</c> itself.</returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.FromTenant,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol.ContainingType?.Name ?? "")
            .Collect()
            .Combine(features)
            .Select(static (pair, _) => (Count: pair.Left.Length, pair.Right.HasComposition));

        context.RegisterSourceOutputSafe(declarations, static (ctx, data) =>
        {
            if (data.Count == 0 || !data.HasComposition)
                return;

            ctx.AddSource(new TenantBindingsMetadataTemplate(data.Count).RenderOutput());
        });

        return declarations.Select(static (data, _) => data.Count == 0 || !data.HasComposition
            ? EquatableArray<Composition.Models.MetadataEntry>.Empty
            : new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.CreatePayload(
                    Composition.MetadataCategoryIds.TenantBindings,
                    SchemaVersion,
                    TenantBindingsMetadataTemplate.Payload(data.Count)))));
    }
}
