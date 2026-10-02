using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Read-contract feature (#3): collects <c>[Published]</c> queries, groups them by boundary, and emits one
///     <c>I{Module}Reads</c> interface + implementation + DI registration per boundary so another module can
///     enforce a cross-boundary invariant by injecting the contract — acyclically.
/// </summary>
/// <remarks>
///     ⚠️ Beside those three it publishes a metadata entry, through both channels, as the lookup
///     caches and the roll-up rules do: an assembly attribute for a referenced module, and a local
///     entry for the host's own compilation. Without it the host has nothing to read and calls
///     nothing: an action injecting the contract fails at resolution, on the <b>first request</b>
///     rather than at startup.
/// </remarks>
internal static class ReadContractFeature
{
    private const string PublishedAttributeFullName = "Pragmatic.Persistence.Query.Attributes.PublishedAttribute";

    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context)
    {
        var published = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PublishedAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                PublishedQueryTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        context.RegisterSourceOutputSafe(published, static (ctx, models) =>
        {
            if (models.IsDefaultOrEmpty)
                return;

            foreach (var group in models.Where(m => m.IsValid).GroupBy(m => m.ContractName))
            {
                var queries = group.ToList();
                if (ReportCollisions(ctx, queries))
                    continue;

                var first = queries[0];
                var template = new ReadContractTemplate(first.ContractName, first.ContractNamespace, queries);
                var artifact = template.RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }

            // The assembly attributes a referenced module publishes. The MetadataEntry below covers
            // only the host's own compilation; without these a module's contract is generated, its
            // registration emitted, and nobody calls it.
            var metadata = new ReadContractMetadataTemplate(RegistrationMethods(models)).RenderOutput();
            if (!metadata.IsEmpty)
                ctx.AddSource(metadata);
        });

        // Under the same condition that emits the contracts, tell the host to bind them.
        return published.Select(static (models, _) =>
        {
            var methods = RegistrationMethods(models);
            if (methods.Count == 0)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return new EquatableArray<Composition.Models.MetadataEntry>(
                methods
                    .Select(m => Composition.Models.HostLocalRegistration.Create(
                        Composition.MetadataCategoryIds.ReadContracts, "1.0", m))
                    .ToImmutableArray());
        });
    }


    /// <summary>
    ///     Two queries contributing one method name: reported, and the contract is not emitted.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The registration list below filters on the same predicate. A contract that is reported
    ///     and skipped but still named in the host metadata would make the host call an
    ///     <c>Add…Reads</c> that nothing emits — a defect in a second module, produced by an error in
    ///     this one.
    /// </remarks>
    private static bool ReportCollisions(SourceProductionContext ctx, IReadOnlyList<Models.PublishedQueryModel> queries)
    {
        var collided = false;

        foreach (var byName in queries.GroupBy(q => q.MethodName).Where(g => g.Count() > 1))
        {
            var duplicates = byName.ToList();
            for (var i = 1; i < duplicates.Count; i++)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.QueryPipelineDiagnostics.PublishedQueryMethodNameCollides,
                    duplicates[i].Location?.ToLocation(),
                    duplicates[0].QueryTypeShortName,
                    duplicates[i].QueryTypeShortName,
                    byName.Key,
                    duplicates[0].ContractName));
            }

            collided = true;
        }

        return collided;
    }

    private static bool HasCollision(IReadOnlyList<Models.PublishedQueryModel> queries)
        => queries.GroupBy(q => q.MethodName).Any(g => g.Count() > 1);

    /// <summary>
    ///     The fully qualified <c>Add{Module}Reads</c> of every contract this assembly publishes.
    /// </summary>
    /// <remarks>
    ///     Built from the same grouping and the same naming the template uses, so the name written
    ///     into the metadata and the name emitted beside the contract cannot drift apart.
    /// </remarks>
    private static IReadOnlyList<string> RegistrationMethods(
        ImmutableArray<Models.PublishedQueryModel> models)
    {
        if (models.IsDefaultOrEmpty)
            return [];

        return models
            .Where(m => m.IsValid)
            .GroupBy(m => m.ContractName)
            .Where(g => !HasCollision(g.ToList()))
            .Select(g =>
            {
                var first = g.First();
                var impl = ReadContractTemplate.ImplementationNameFor(first.ContractName);
                var container = string.IsNullOrEmpty(first.ContractNamespace)
                    ? $"{impl}RegistrationExtensions"
                    : $"{first.ContractNamespace}.{impl}RegistrationExtensions";

                return $"{container}.Add{impl}";
            })
            .ToList();
    }
}
