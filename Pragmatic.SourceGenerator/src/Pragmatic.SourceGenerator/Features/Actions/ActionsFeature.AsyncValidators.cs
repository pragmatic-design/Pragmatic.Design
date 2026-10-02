using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Which operations run an async validator, decided here rather than looked up at runtime.
/// </summary>
/// <remarks>
///     <para>
///         Without validation metadata, a mutation's runtime would ask the container for
///         <c>IAsyncValidator&lt;TMutation&gt;</c> on every call — a lookup whose <c>null</c> cannot be
///         told from "no validator, by design" (docs/CONVENTIONS.md, «Decide at compile time»). And an
///         action that ran one only with <c>[Validate]</c> would leave a <c>[Validator]</c> for it
///         registered and never called.
///     </para>
///     <para>
///         One rule, for both: a <c>[Validator]</c> class implementing <c>IAsyncValidator&lt;T&gt;</c>
///         in this compilation is the opt-in for <c>T</c>, and <c>[Validate]</c> changes the default.
///         The set is read from the attribute directly — the Validation feature reads the same classes
///         for registration, and asking it would be one generator depending on another's output.
///         A validator declared in another assembly than its operation is outside the set by
///         construction; the Validation feature reports it (PRAG0215).
///     </para>
/// </remarks>
internal static partial class ActionsFeature
{
    private const string ValidatorAttributeFullName = "Pragmatic.Validation.Attributes.ValidatorAttribute";

    /// <summary>The fully qualified names of every type a <c>[Validator]</c> in this compilation validates asynchronously.</summary>
    private static IncrementalValueProvider<EquatableArray<string>> AsyncValidatedTypes(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidatorAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AsyncValidatorTargets(ctx.TargetSymbol))
            .SelectMany(static (targets, _) => targets)
            .Collect()
            .Select(static (all, _) => all.Distinct().OrderBy(t => t, System.StringComparer.Ordinal).ToEquatableArray());

    /// <summary>The types <paramref name="validator" /> implements <c>IAsyncValidator&lt;T&gt;</c> for.</summary>
    internal static ImmutableArray<string> AsyncValidatorTargets(ISymbol validator)
        => validator is INamedTypeSymbol type
            ? type.AllInterfaces
                .Where(i => i.Name == "IAsyncValidator"
                            && i.TypeArguments.Length == 1
                            && i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Validation")
                .Select(i => i.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;

    /// <summary>
    ///     The validation metadata of a mutation, always emitted: without it the runtime falls back to
    ///     asking the container, which is the lookup this replaces.
    /// </summary>
    private static void GenerateMutationValidationMetadata(
        SourceProductionContext context, MutationModel model, EquatableArray<string> asyncValidatedTypes)
    {
        var validationModel = new ActionValidationModel
        {
            TypeName = model.TypeName,
            FullTypeName = model.FullTypeName,
            Namespace = model.Namespace,
            Accessibility = model.Accessibility,
            HasNoValidation = model.HasNoValidation,
            RunSync = model.RunSync,
            RunAsync = model.ValidateIsDeclared
                ? model.RunAsync
                : asyncValidatedTypes.AsImmutableArray().Contains(model.FullTypeName),
        };

        context.AddSource(new ValidationMetadataTemplate(validationModel).RenderOutput());
    }
}
