using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;

namespace Pragmatic.SourceGenerator.Features.Glossary;

/// <summary>
///     Standalone feature: emits a module's use-case catalog from <c>[UseCase]</c> and <c>[Rule]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Both attributes are declared in <c>Pragmatic.Abstractions</c>, both are public
///         and documented, and their summaries name "tooling, analyzers, and the generated use-case
///         catalog" as their consumers. This is that catalog.
///     </para>
///     <para>
///         Registered without a <c>DetectedFeatures</c> gate, like the three documentation features
///         beside it: the attributes live in Abstractions, which every module references, so there is
///         no capability to detect. The gate that keeps it quiet is having nothing to say — a
///         compilation that writes neither attribute produces no file.
///     </para>
/// </remarks>
internal static class UseCaseCatalogFeature
{
    private const string UseCaseAttributeMetadataName = "Pragmatic.Authoring.UseCaseAttribute";
    private const string RuleAttributeMetadataName = "Pragmatic.Authoring.RuleAttribute";

    /// <summary>The MSBuild property that turns an absolute source path into a readable one.</summary>
    private const string ProjectDirProperty = "build_property.projectdir";

    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var withUseCase = Collect(context, UseCaseAttributeMetadataName, static (symbol, ct) => FromUseCase(symbol, ct));

        // A [Rule] on a member that also carries [UseCase] is already collected above, with its use
        // case. Only the orphans come through here — dropping them instead would leave [Rule] exactly
        // where this issue found it: declared, documented and read by nobody.
        var rulesOnly = Collect(context, RuleAttributeMetadataName, static (symbol, ct) =>
            symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == UseCaseAttributeMetadataName)
                ? null
                : FromRulesAlone(symbol, ct));

        var entries = withUseCase.Collect()
            .Combine(rulesOnly.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        var projectDir = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue(ProjectDirProperty, out var value) ? value : "");

        var assemblyName = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "");

        context.RegisterSourceOutputSafe(entries.Combine(projectDir).Combine(assemblyName), static (spc, data) =>
        {
            var ((models, dir), assembly) = data;
            var valid = models.Where(static m => m is not null).Select(static m => m!).ToImmutableArray();
            if (valid.Length == 0)
                return;

            var artifact = new UseCaseCatalogTemplate(valid, assembly, dir).RenderOutput();
            if (!artifact.IsEmpty)
                spc.AddSource(artifact);
        });
    }

    private static IncrementalValuesProvider<UseCaseModel?> Collect(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName,
        System.Func<ISymbol, CancellationToken, UseCaseModel?> transform)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeMetadataName,
            predicate: static (node, _) => node is TypeDeclarationSyntax or MethodDeclarationSyntax,
            transform: (ctx, ct) => transform(ctx.TargetSymbol, ct));

    private static UseCaseModel? FromUseCase(ISymbol symbol, CancellationToken ct)
    {
        var useCase = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UseCaseAttributeMetadataName);
        if (useCase is null)
            return null;

        var id = useCase.ConstructorArguments.Length > 0 ? useCase.ConstructorArguments[0].Value as string : null;
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var title = useCase.NamedArguments.FirstOrDefault(static a => a.Key == "Title").Value.Value as string;

        return Build(symbol, id, title, ct);
    }

    private static UseCaseModel? FromRulesAlone(ISymbol symbol, CancellationToken ct)
        => Build(symbol, id: null, title: null, ct);

    private static UseCaseModel? Build(ISymbol symbol, string? id, string? title, CancellationToken ct)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location is null)
            return null;

        ct.ThrowIfCancellationRequested();

        var span = location.GetLineSpan();
        var rules = symbol.GetAttributes()
            .Where(static a => a.AttributeClass?.ToDisplayString() == RuleAttributeMetadataName)
            .Select(static a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(static text => text!)
            .ToImmutableArray();

        // Rules alone is the only reason FromRulesAlone runs, so an empty list there means every
        // [Rule] on the member was written with an empty string — nothing to record.
        if (id is null && rules.Length == 0)
            return null;

        return new UseCaseModel
        {
            Id = id,
            Title = title,
            Target = TargetName(symbol),
            SourceFile = span.Path,
            Line = span.StartLinePosition.Line + 1,
            Rules = rules
        };
    }

    /// <summary>
    ///     The member's fully qualified name, without <c>global::</c> and without a parameter list.
    /// </summary>
    /// <remarks>
    ///     <c>FullyQualifiedFormat</c> spells a method's parameters fully qualified too
    ///     (<c>Dispense(global::System.Int32)</c>), which is unambiguous and unreadable. A catalog is
    ///     read by people; the type and method name is what they search for.
    /// </remarks>
    private static string TargetName(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method && method.ContainingType is not null)
            return $"{Qualified(method.ContainingType)}.{method.Name}";

        return Qualified(symbol);
    }

    private static string Qualified(ISymbol symbol)
    {
        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name.StartsWith("global::", System.StringComparison.Ordinal) ? name.Substring("global::".Length) : name;
    }
}
