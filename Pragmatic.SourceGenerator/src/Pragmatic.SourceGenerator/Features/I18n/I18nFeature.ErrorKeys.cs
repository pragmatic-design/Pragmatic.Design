using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.I18n.Diagnostics;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n;

/// <summary>
///     Checks every <c>MessageKey</c> an error type declares against the translation files.
/// </summary>
/// <remarks>
///     A failure mode the rest of the error pipeline cannot catch: the resolver takes
///     <c>MessageKey</c> verbatim, so a key nobody defined returns the default text instead of
///     failing. Nothing logs it. This turns it into a build warning.
/// </remarks>
internal static partial class I18nFeature
{
    /// <summary>
    ///     Suffix of the key the resolver reads for the message a caller sees. Kept in sync with
    ///     <c>LocalizedErrorMessageResolver.DetailSuffix</c>: the generator targets netstandard2.0
    ///     and cannot reference the runtime assembly that declares it.
    /// </summary>
    private const string DetailSuffix = ".detail";

    /// <summary>A declared <c>MessageKey</c> and where it was written.</summary>
    internal readonly record struct DeclaredMessageKey(string TypeName, string Key, LocationInfo? Location);

    /// <summary>
    ///     Every type that overrides <c>MessageKey</c> with a string literal.
    /// </summary>
    /// <remarks>
    ///     <c>TypeDeclarationSyntax</c>, not <c>ClassDeclarationSyntax</c>: an error is normally a
    ///     record, and a class-only predicate would silently skip every one of them.
    ///     A computed key (interpolation, a constant reference) is not read — there is nothing to
    ///     compare — and is left alone rather than guessed at.
    /// </remarks>
    internal static IncrementalValueProvider<EquatableArray<DeclaredMessageKey>> DeclaredMessageKeys(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Extract(ctx))
            .Where(static k => k is not null)
            .Select(static (k, _) => k!.Value)
            .Collect()
            .Select(static (all, _) => new EquatableArray<DeclaredMessageKey>(all));

    /// <summary>Prefix the runtime puts in front of a key derived from a code.</summary>
    /// <remarks>
    ///     Kept in sync with <c>Error.MessageKey</c>, which the generator cannot reference for the
    ///     same reason as <see cref="DetailSuffix" />: netstandard2.0 cannot see the runtime assembly.
    /// </remarks>
    private const string DerivedKeyPrefix = "error.";

    private static DeclaredMessageKey? Extract(GeneratorSyntaxContext ctx)
    {
        var type = (TypeDeclarationSyntax)ctx.Node;

        return FromMessageKeyProperty(type)
               ?? FromBusinessRuleBaseCall(type)
               ?? FromCodeOverride(ctx, type);
    }

    /// <summary>
    ///     An error that overrides <c>Code</c> alone, and therefore inherits a derived key.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The shape the documentation shows and the one almost every error uses.
    ///         <c>Error.MessageKey</c> is virtual with a derived default —
    ///         <c>"error." + Code.ToLowerInvariant().Replace('_', '.')</c> — so a type naming only its
    ///         code has a key the two extractors above never read. The check ran, found nothing to
    ///         check, and reported nothing; measured on a consumer application whose three modules
    ///         ship translation files specifically so this would run and whose build emitted zero
    ///         <c>PRAG1804</c>, because a search for <c>MessageKey</c> across its source returns
    ///         nothing at all.
    ///     </para>
    ///     <para>
    ///         An explicit <c>MessageKey</c> keeps winning, and the guard is the <b>presence</b> of the
    ///         property rather than its readability: a computed one is unreadable and must stay
    ///         unread, never fall through to a key its code would have derived and the runtime would
    ///         not use.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>override</c> is required. <c>Error.Code</c> is abstract, so that is the only form
    ///         that answers for the base — and unlike <c>MessageKey</c>, <c>Code</c> is a name any
    ///         type might carry for reasons of its own.
    ///     </para>
    /// </remarks>
    private static DeclaredMessageKey? FromCodeOverride(GeneratorSyntaxContext ctx, TypeDeclarationSyntax type)
    {
        var properties = type.Members.OfType<PropertyDeclarationSyntax>().ToList();

        if (properties.Any(p => p.Identifier.ValueText == "MessageKey"))
            return null;

        var code = properties.FirstOrDefault(p =>
            p.Identifier.ValueText == "Code" &&
            p.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)));

        if (code?.ExpressionBody?.Expression is not LiteralExpressionSyntax literal)
            return null;

        if (literal.Token.Value is not string value || value.Length == 0)
            return null;

        // Asked last, because it is the only question here that costs anything: the syntax above
        // narrows this to a handful of types per compilation instead of every one with a base list.
        if (!DerivesFromAnError(ctx, type))
            return null;

        var key = DerivedKeyPrefix + value.ToLowerInvariant().Replace('_', '.');

        return new DeclaredMessageKey(type.Identifier.ValueText, key, LocationInfo.From(code.GetLocation()));
    }

    /// <summary>
    ///     Whether the type's base chain reaches something called <c>Error</c>.
    /// </summary>
    /// <remarks>
    ///     By name rather than by fully-qualified name, which is the same compromise
    ///     <see cref="FromBusinessRuleBaseCall" /> makes and for the same reason: the generator
    ///     targets netstandard2.0 and cannot reference <c>Pragmatic.Result</c>, and matching
    ///     <c>Pragmatic.Result.Error</c> as a string would make the check blind to every application —
    ///     and every test — that models its errors on a base of its own.
    ///     <para>
    ///         The chain rather than the base list, because <c>SomethingError : DomainError : Error</c>
    ///         is the ordinary shape and a one-level check would miss it.
    ///     </para>
    /// </remarks>
    private static bool DerivesFromAnError(GeneratorSyntaxContext ctx, TypeDeclarationSyntax type)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(type) is not INamedTypeSymbol symbol)
            return false;

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "Error")
                return true;
        }

        return false;
    }

    /// <summary>An error that overrides <c>MessageKey</c> with a literal.</summary>
    private static DeclaredMessageKey? FromMessageKeyProperty(TypeDeclarationSyntax type)
    {
        var property = type.Members
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.ValueText == "MessageKey");

        if (property?.ExpressionBody?.Expression is not LiteralExpressionSyntax literal)
            return null;

        if (literal.Token.Value is not string key || key.Length == 0)
            return null;

        return new DeclaredMessageKey(type.Identifier.ValueText, key, LocationInfo.From(property.GetLocation()));
    }

    /// <summary>
    ///     A rule deriving from <c>BusinessRuleError</c>, which names itself by passing the rule to
    ///     the base constructor — and inherits a computed <c>MessageKey</c>, so the property check
    ///     above never sees it.
    /// </summary>
    private static DeclaredMessageKey? FromBusinessRuleBaseCall(TypeDeclarationSyntax type)
    {
        if (type.BaseList is null ||
            !type.BaseList.Types.Any(t => t.Type.ToString().EndsWith("BusinessRuleError", StringComparison.Ordinal)))
            return null;

        foreach (var ctor in type.Members.OfType<ConstructorDeclarationSyntax>())
        {
            if (ctor.Initializer is not { ArgumentList.Arguments.Count: > 0 } initializer)
                continue;

            if (initializer.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax lit ||
                lit.Token.Value is not string rule || rule.Length == 0)
                continue;

            return new DeclaredMessageKey(type.Identifier.ValueText, rule, LocationInfo.From(ctor.GetLocation()));
        }

        return null;
    }

    /// <summary>
    ///     Reports <see cref="I18NDiagnostics.MessageKeyNotTranslated" /> for keys no file defines.
    /// </summary>
    private static void ReportUntranslatedMessageKeys(
        SourceProductionContext context,
        TranslationKeysGenerationModel? model,
        EquatableArray<DeclaredMessageKey> declared)
    {
        // No declared keys is the one case with nothing to say: it separates "nothing is translated"
        // from "there is nothing to translate".
        if (declared.AsImmutableArray().IsDefaultOrEmpty)
            return;

        // ⚠️ A null model — no translation files at all — is not an early return, neither in Generate
        // nor here when the known set comes back empty. Both would read as obvious optimisations and
        // both would silence the check for the application that most needs it, the one that has
        // translated nothing. The set is empty here, which is exactly the answer that makes every
        // declared key untranslated.
        var known = model is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(Flatten(model), StringComparer.Ordinal);

        foreach (var entry in declared.AsImmutableArray())
        {
            // The message the caller reads lives under '{key}.detail' — mirrors
            // LocalizedErrorMessageResolver.DetailSuffix, which the generator cannot reference.
            // A missing title falls back to the error code and is not what makes the text English
            // in every language, so the detail is what this check is about.
            if (known.Contains(entry.Key + DetailSuffix))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                I18NDiagnostics.MessageKeyNotTranslated,
                entry.Location?.ToLocation(),
                entry.TypeName,
                entry.Key));
        }
    }

    private static IEnumerable<string> Flatten(TranslationKeysGenerationModel model)
    {
        foreach (var key in model.RootKeys.AsImmutableArray())
            yield return key.FullKey;

        foreach (var group in model.Groups.AsImmutableArray())
            foreach (var key in FlattenGroup(group))
                yield return key;
    }

    private static IEnumerable<string> FlattenGroup(TranslationKeyGroupModel group)
    {
        foreach (var key in group.Keys.AsImmutableArray())
            yield return key.FullKey;

        foreach (var nested in group.NestedGroups.AsImmutableArray())
            foreach (var key in FlattenGroup(nested))
                yield return key;
    }
}
