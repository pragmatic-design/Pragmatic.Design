using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.FeatureFlags.Templates;

namespace Pragmatic.SourceGenerator.Features.FeatureFlags;

/// <summary>
///     Publishes whether an assembly <b>declares</b> a feature flag, so a host wires the capability
///     because somebody named one rather than because the package is on the compilation.
/// </summary>
/// <remarks>
///     <para>
///         The sixth capability moved off presence, and it was left out of the first five because
///         nobody had checked whether it had a declaration site. It does:
///         <c>IFeatureFlag</c> is a public interface an application implements to name a flag.
///         Measured before this existed — a host that references the package and declares nothing
///         still received <c>services.AddPragmaticFeatureFlags()</c>, and with it an in-memory store
///         and a scoped <c>IFeatureFlags</c> nobody resolves.
///     </para>
///     <para>
///         ⚠️ <b>Collected by interface, not by attribute</b>, which is the one thing that differs
///         from its five neighbours and the reason this feature does not use
///         <c>ForAttributeWithMetadataName</c>. A reader who arrives here expecting the shape of
///         <c>ResilienceFeature</c> will not find it: the cheap syntax test is "a type declaration
///         with a base list", and the question that costs something — does that base list reach
///         <c>Pragmatic.FeatureFlags.IFeatureFlag</c> — is asked afterwards, of the semantic model.
///     </para>
///     <para>
///         The interface is matched by its full name, unlike the loose name matching some other
///         readers use: it lives in <c>Pragmatic.Abstractions</c>, which every application
///         references, so there is no case where the real one is out of reach and a namesake has to
///         stand in for it.
///     </para>
///     <para>
///         Two channels, and both are needed: the assembly attribute is how a <b>referenced module</b>
///         is discovered, and the <see cref="MetadataEntry" /> returned here is how the <b>host's own</b>
///         declarations reach the same aggregation — the attribute this run emits is not on the symbol
///         yet, and the reader only walks <c>compilation.References</c>.
///     </para>
///     <para>
///         Nothing is emitted when nothing is declared. An empty document would put the category on
///         the compilation and put the question back where it started.
///     </para>
/// </remarks>
internal static class FeatureFlagsFeature
{
    private const string FlagInterface = "Pragmatic.FeatureFlags.IFeatureFlag";

    public static IncrementalValueProvider<EquatableArray<MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (ctx, _) => IsFlag(ctx) ? 1 : 0)
            .Where(static declared => declared == 1)
            .Collect();

        var withAssembly = declarations
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? ""))
            .Select(static (pair, _) => (Count: pair.Left.Length, Assembly: pair.Right));

        context.RegisterSourceOutputSafe(withAssembly, static (ctx, data) =>
        {
            if (data.Count == 0)
                return;

            ctx.AddSource(new FeatureFlagsMetadataTemplate(data.Count).RenderOutput());
        });

        return withAssembly.Select(static (data, _) => data.Count == 0
            ? EquatableArray<MetadataEntry>.Empty
            : new EquatableArray<MetadataEntry>(ImmutableArray.Create(
                HostLocalRegistration.CreatePayload(
                    MetadataCategoryIds.FeatureFlags,
                    "1.0",
                    FeatureFlagsMetadataTemplate.Payload(data.Count)))));
    }

    /// <summary>
    ///     Whether this type declaration implements <c>IFeatureFlag</c>.
    /// </summary>
    /// <remarks>
    ///     <c>AllInterfaces</c> rather than the base list, because a flag declared through a base
    ///     class of the application's own is still a flag — and because the base list is syntax, which
    ///     cannot tell an interface from a class it happens to name.
    /// </remarks>
    private static bool IsFlag(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol symbol)
            return false;

        if (symbol.IsAbstract)
            return false;

        foreach (var contract in symbol.AllInterfaces)
        {
            if (contract.ToDisplayString() == FlagInterface)
                return true;
        }

        return false;
    }
}
