using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;

namespace Pragmatic.SourceGenerator.Features.Glossary;

/// <summary>
///     Standalone feature: generates a C4 container diagram (Mermaid) of the host's composed modules,
///     read from <c>[Include&lt;TModule&gt;]</c> (all arities, in-process) and
///     <c>[RemoteBoundary&lt;TModule&gt;]</c> (remote). Emitted as a compile-time constant.
/// </summary>
internal static class ArchitectureFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var inc1 = Collect(context, "Pragmatic.Composition.Attributes.IncludeAttribute`1", isRemote: false);
        var inc2 = Collect(context, "Pragmatic.Composition.Attributes.IncludeAttribute`2", isRemote: false);
        var inc3 = Collect(context, "Pragmatic.Composition.Attributes.IncludeAttribute`3", isRemote: false);
        var remote = Collect(context, "Pragmatic.Composition.Attributes.RemoteBoundaryAttribute`1", isRemote: true);

        var modules = inc1.Collect()
            .Combine(inc2.Collect())
            .Combine(inc3.Collect())
            .Combine(remote.Collect())
            .Select(static (t, _) =>
            {
                var (((a, b), c), d) = t;
                return a.Concat(b).Concat(c).Concat(d).SelectMany(static x => x).ToImmutableArray();
            });

        var withAssembly = modules.Combine(
            context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? ""));

        context.RegisterSourceOutputSafe(withAssembly, static (spc, data) =>
        {
            var (mods, assemblyName) = data;
            if (mods.Length == 0)
                return;

            var artifact = new C4DiagramTemplate(mods, assemblyName).RenderOutput();
            if (!artifact.IsEmpty)
                spc.AddSource(artifact);
        });
    }

    private static IncrementalValuesProvider<EquatableArray<C4ModuleModel>> Collect(
        IncrementalGeneratorInitializationContext context, string attributeMetadataName, bool isRemote)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeMetadataName,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: (ctx, _) => ExtractModules(ctx, isRemote));

    private static EquatableArray<C4ModuleModel> ExtractModules(GeneratorAttributeSyntaxContext ctx, bool isRemote)
    {
        var builder = ImmutableArray.CreateBuilder<C4ModuleModel>();
        foreach (var attribute in ctx.Attributes)
        {
            if (attribute.AttributeClass is { TypeArguments.Length: > 0 } ac
                && ac.TypeArguments[0] is INamedTypeSymbol module)
                builder.Add(new C4ModuleModel { Name = module.Name, IsRemote = isRemote });
        }

        return builder.ToImmutable();
    }
}
