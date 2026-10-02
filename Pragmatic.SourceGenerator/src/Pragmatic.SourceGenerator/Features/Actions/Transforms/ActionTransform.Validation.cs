using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Validation metadata parsing, permission/policy extraction from action attributes.
/// </summary>
internal static partial class ActionTransform
{
    // =========================================================================
    // Validation Metadata (BRIDGE elimination)
    // =========================================================================

    private const string NoValidationAttributeName = "Pragmatic.Actions.Attributes.NoValidationAttribute";
    private const string ValidateAttributeName = "Pragmatic.Actions.Attributes.ValidateAttribute";
    private const string ISyncValidatorName = "Pragmatic.Validation.ISyncValidator";

    /// <summary>
    ///     What the operation declares about its own validation. <c>ValidateIsDeclared</c> tells an
    ///     explicit <c>[Validate]</c> from its absence: without it, whether async validation runs is
    ///     decided later, from the <c>[Validator]</c> classes of the compilation.
    /// </summary>
    internal static (bool HasNoValidation, bool RunSync, bool RunAsync,
        ImmutableArray<NestedValidatorProperty> SyncNestedProps,
        ImmutableArray<AsyncNestedValidatorProperty> AsyncNestedProps,
        bool ValidateIsDeclared)
        ParseValidationMetadata(INamedTypeSymbol symbol, Compilation compilation)
    {
        var (hasNoValidation, runSync, runAsync, validateIsDeclared) = ParseDeclaredValidation(symbol);
        var (syncNestedProps, asyncNestedProps) = ParseNestedValidation(symbol, compilation, runAsync);
        return (hasNoValidation, runSync, runAsync, syncNestedProps, asyncNestedProps, validateIsDeclared);
    }

    /// <summary>
    ///     <c>[NoValidation]</c> and <c>[Validate]</c> as the operation declares them — the half a
    ///     mutation shares with an action. A mutation's children are validated by its own nested tree,
    ///     so it does not need the nested-property selection below.
    /// </summary>
    internal static (bool HasNoValidation, bool RunSync, bool RunAsync, bool ValidateIsDeclared)
        ParseDeclaredValidation(INamedTypeSymbol symbol)
    {
        var hasNoValidation = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == NoValidationAttributeName);

        var runSync = true;
        var runAsync = false;

        var validateAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ValidateAttributeName);

        if (validateAttr is not null)
        {
            // [Validate] attribute defaults: Async=true, Sync=true (matching ValidateAttribute property defaults).
            // NamedArguments only contains explicitly set values, so bare [Validate] needs these defaults.
            runAsync = true;

            foreach (var arg in validateAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "Sync" when arg.Value.Value is bool s:
                        runSync = s;
                        break;
                    case "Async" when arg.Value.Value is bool a:
                        runAsync = a;
                        break;
                    case "AsyncOnly" when arg.Value.Value is true:
                        runSync = false;
                        runAsync = true;
                        break;
                }
            }
        }

        return (hasNoValidation, runSync, runAsync, validateAttr is not null);
    }

    private static (ImmutableArray<NestedValidatorProperty> Sync, ImmutableArray<AsyncNestedValidatorProperty> Async)
        ParseNestedValidation(INamedTypeSymbol symbol, Compilation compilation, bool runAsync)
    {
        // ── The nested validatable properties: **one** selection ─────────────────────────────
        //
        // The question is asked once: «does this property, or its element type, carry rules?». The
        // answer is predicted, as Persistence does with TraitPropertyResolver — see ValidatablePredictor.
        // Asking the symbol instead fails both ways: `prop.Type.AllInterfaces.Contains(ISyncValidator)`
        // is always false for a type of the current compilation, because another generator adds that
        // interface; and resolving `IAsyncValidator<List<T>>` asks for the validator **of the list**,
        // which nobody registers.
        //
        // ⚠️ The two **consumers** stay separate, and that is not duplication: they sit at two points of
        // the pipeline (L1 sync, without DI; L1 async, with the service provider), and `IValidator<T>` —
        // which would compose the two — is registered only for types with a [Validator] class or async
        // bindings. Resolving it from the container would leave the most common case uncovered: a child
        // that validates by attributes alone.
        var nestedProps = ImmutableArray.CreateBuilder<NestedValidatorProperty>();

        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;

            if (Validation.Analysis.ValidatablePredictor.WillHaveSyncValidator(prop.Type, compilation))
            {
                nestedProps.Add(new NestedValidatorProperty(
                    prop.Name,
                    prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            // A collection is judged by its element type: the element carries the rules, and any async
            // validator is resolved for it.
            if (Validation.Analysis.ValidatablePredictor.ElementOf(prop.Type) is { } element
                && Validation.Analysis.ValidatablePredictor.WillHaveSyncValidator(element, compilation))
            {
                nestedProps.Add(new NestedValidatorProperty(
                    prop.Name,
                    element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsCollection: true));
            }
        }

        var syncNestedProps = nestedProps;
        var asyncNestedProps = ImmutableArray.CreateBuilder<AsyncNestedValidatorProperty>();

        if (runAsync)
            foreach (var nested in nestedProps)
                asyncNestedProps.Add(new AsyncNestedValidatorProperty(
                    nested.PropertyName, nested.PropertyTypeName, nested.IsCollection));

        return (syncNestedProps.ToImmutable(), asyncNestedProps.ToImmutable());
    }

    /// <summary>
    ///     Parses [RequirePolicy&lt;TPolicy&gt;] attribute to extract the policy type FQN.
    /// </summary>
    internal static string? ParseRequirePolicy(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            // Use Name + ContainingNamespace instead of ToDisplayString() which returns
            // backtick-arity format for generic OriginalDefinition (e.g. "RequirePolicyAttribute`1").
            var origDef = attrClass.OriginalDefinition;
            if (origDef.Name != "RequirePolicyAttribute" ||
                origDef.ContainingNamespace?.ToDisplayString() != "Pragmatic.Authorization.Policy")
                continue;

            if (attrClass.TypeArguments.Length > 0)
                return attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }

    internal const string RequirePermissionAttrName = "Pragmatic.Authorization.RequirePermissionAttribute";
    internal const string RequireAnyPermissionAttrName = "Pragmatic.Authorization.RequireAnyPermissionAttribute";

    /// <summary>
    ///     Parses [RequirePermission] and [RequireAnyPermission] attributes to extract permission names.
    ///     Returns both the resolved literal permissions and the unresolved constant paths (e.g.
    ///     <c>BookingPermissions.GuestPreferences.Update</c>) — the latter are resolved later against the
    ///     permission catalog, because a source generator cannot resolve the constants it generates
    ///     itself in the same compilation (they would otherwise fail-open).
    /// </summary>
    internal static (ImmutableArray<string> RequireAll, ImmutableArray<string> RequireAny,
        ImmutableArray<string> UnresolvedAll, ImmutableArray<string> UnresolvedAny) ParseRequirePermissions(
        INamedTypeSymbol symbol, Compilation compilation)
    {
        var requireAll = ImmutableArray<string>.Empty;
        var requireAny = ImmutableArray<string>.Empty;
        var unresolvedAll = ImmutableArray<string>.Empty;
        var unresolvedAny = ImmutableArray<string>.Empty;

        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var attrName = attrClass.ToDisplayString();

            if (attrName == RequirePermissionAttrName)
                (requireAll, unresolvedAll) = ExtractPermissionStrings(attr, compilation);
            else if (attrName == RequireAnyPermissionAttrName)
                (requireAny, unresolvedAny) = ExtractPermissionStrings(attr, compilation);
        }

        return (requireAll, requireAny, unresolvedAll, unresolvedAny);
    }

    /// <summary>
    ///     The names of the permission attributes that were applied with no permission at all.
    ///     <para>
    ///         Such an attribute leaves no trace downstream — nothing is extracted, so the type never
    ///         enters the permission requirement registry and the pipeline cannot tell it apart from an
    ///         action that declared nothing. The fact has to be carried explicitly from here, because
    ///         this is the last point where it is still observable.
    ///     </para>
    /// </summary>
    internal static ImmutableArray<string> ParseEmptyPermissionAttributes(INamedTypeSymbol symbol, Compilation compilation)
    {
        var empty = ImmutableArray.CreateBuilder<string>();

        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName != RequirePermissionAttrName && attrName != RequireAnyPermissionAttrName)
                continue;

            var (resolved, unresolved) = ExtractPermissionStrings(attr, compilation);
            if (resolved.Length == 0 && unresolved.Length == 0)
                empty.Add(attr.AttributeClass!.Name);
        }

        return empty.ToImmutable();
    }

    /// <summary>
    ///     Extracts permission strings from a [RequirePermission]/[RequireAnyPermission] attribute,
    ///     splitting them into resolved literals and unresolved constant paths (captured from syntax).
    /// </summary>
    /// <param name="attr">The attribute.</param>
    /// <param name="compilation">
    ///     The compilation the attribute belongs to: a constant beside a generated one — of a referenced assembly
    ///     or hand-written in this one — is folded through it rather than left as a path the catalogue does not
    ///     hold. Required, so that no reader of the same attribute can give a different answer by omitting it.
    /// </param>
    internal static (ImmutableArray<string> Resolved, ImmutableArray<string> Unresolved) ExtractPermissionStrings(
        AttributeData attr, Compilation compilation)
    {
        var resolved = ImmutableArray.CreateBuilder<string>();
        var unresolved = ImmutableArray.CreateBuilder<string>();

        // Drive by SYNTAX, not by ConstructorArguments: when an argument references a constant this
        // generator itself produces, Roslyn cannot bind it in this compilation and may drop the
        // constructor arguments entirely — so the positional argument syntax is the reliable source.
        var positionalSyntax = GetPositionalArgumentSyntax(attr);
        var semanticValues = GetFlattenedConstructorValues(attr);

        for (var i = 0; i < positionalSyntax.Count; i++)
        {
            // Prefer the resolved value when Roslyn could bind it (literals, resolvable constants).
            if (i < semanticValues.Length && semanticValues[i].Value is string s)
            {
                resolved.Add(s);
                continue;
            }

            // ⚠️ A literal does NOT always bind above. When another argument names a constant this run
            // generates, Roslyn drops every constructor argument — the literal's value with it — and the
            // literal was captured below as its quoted text, looked up as a path, and reported unresolvable
            // (PRAG0418 on a [RequirePermission("x", Gen.Const)], PRAG1008 on a [Grants]). Its value is in
            // the syntax.
            var expression = positionalSyntax[i];
            if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                resolved.Add(literal.Token.ValueText);
                continue;
            }

            // The same drop takes every other constant with it — of a referenced assembly, or hand-written here —
            // which the compiler can still fold on its own: only a constant this run generates is left for the
            // catalogue. A tree the compilation does not hold has no model to ask.
            if (compilation.ContainsSyntaxTree(expression.SyntaxTree)
                && compilation.GetSemanticModel(expression.SyntaxTree).GetConstantValue(expression)
                    is { HasValue: true, Value: string folded })
            {
                resolved.Add(folded);
                continue;
            }

            // Otherwise it is an unresolvable (generated) permission constant — capture the source path
            // so the resolution stage can look it up against the permission catalog.
            var path = expression.ToString();
            if (!string.IsNullOrEmpty(path))
                unresolved.Add(path); // e.g. "BookingPermissions.GuestPreferences.Update"
        }

        return (resolved.ToImmutable(), unresolved.ToImmutable());
    }

    internal const string ExplicitPermissionAttrName = "Pragmatic.Authorization.ExplicitPermissionAttribute";

    /// <summary>
    ///     Parses <c>[ExplicitPermission("name")]</c> / <c>[ExplicitPermission(SomeGeneratedConst)]</c>. Nothing
    ///     is resolved here: the argument goes through <see cref="ExtractPermissionStrings" /> so a generated
    ///     constant survives as a path, reachable once the permission catalog has been built.
    /// </summary>
    internal static Models.ExplicitPermissionModel? ParseExplicitPermission(INamedTypeSymbol symbol, Compilation compilation)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var origDef = attrClass.OriginalDefinition;
            if (origDef.Name != "ExplicitPermissionAttribute" ||
                origDef.ContainingNamespace?.ToDisplayString() != "Pragmatic.Authorization")
                continue;

            var (resolved, unresolved) = ExtractPermissionStrings(attr, compilation);
            if (resolved.Length > 0)
                return new Models.ExplicitPermissionModel { Value = resolved[0] };
            if (unresolved.Length > 0)
                return new Models.ExplicitPermissionModel { ConstPath = unresolved[0] };
        }

        return null;
    }

    /// <summary>
    ///     Whether the type opts out of authorization entirely. Both spellings are accepted, exactly as
    ///     <c>EndpointTransform.ParseAuthorization</c> accepts them — an action that is anonymous over HTTP
    ///     and permission-bearing in the action pipeline would be a contradiction nobody wrote on purpose.
    /// </summary>
    /// <summary>Whether the operation declares <c>[TenantAgnostic]</c>.</summary>
    /// <remarks>
    ///     Separate from <see cref="ParseAllowAnonymous" /> because the two are separate questions and
    ///     conflating them is the defect: <c>[AllowAnonymous]</c> lifts authentication, and the tenant
    ///     refusal happens before the route runs, whoever is asking.
    /// </remarks>
    internal static bool ParseTenantAgnostic(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == Endpoints.EndpointAttributeNames.TenantAgnostic)
                return true;
        }

        return false;
    }

    internal static bool ParseAllowAnonymous(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == Endpoints.EndpointAttributeNames.AllowAnonymous ||
                name == Endpoints.EndpointAttributeNames.MicrosoftAllowAnonymous)
                return true;
        }

        return false;
    }

    private static ImmutableArray<TypedConstant> GetFlattenedConstructorValues(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length == 0)
            return ImmutableArray<TypedConstant>.Empty;

        // params string[] arrives as one Array-kind TypedConstant whose .Values are the elements.
        return attr.ConstructorArguments[0].Kind == TypedConstantKind.Array
            ? attr.ConstructorArguments[0].Values
            : attr.ConstructorArguments;
    }

    private static List<ExpressionSyntax> GetPositionalArgumentSyntax(AttributeData attr)
    {
        var result = new List<ExpressionSyntax>();
        if (attr.ApplicationSyntaxReference?.GetSyntax() is not
                Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax { ArgumentList: { } argList })
            return result;

        foreach (var arg in argList.Arguments)
        {
            if (arg.NameEquals is not null) // named argument (e.g. Description = "...")
                continue;
            result.Add(arg.Expression);
        }

        return result;
    }
}
