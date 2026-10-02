using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static class InputPropertyHelpers
{
    public static ImmutableArray<ActionPropertyModel> ParseInputProperties(
        INamedTypeSymbol symbol, Compilation compilation)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                p is { IsStatic: false, IsOverride: false, GetMethod: not null, SetMethod: not null } &&
                p.ContainingType.Equals(symbol, SymbolEqualityComparer.Default))
            .Select(p => new ActionPropertyModel
            {
                Name = p.Name,
                TypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsRequired = p.IsRequired,
                IsNullable = IsNullableType(p.Type),
                DefaultValueSyntax = ExtractDefaultValueSyntax(p, compilation),
                DefaultIsCompileTimeConstant = HasConstantInitializer(p, compilation),
                IsBoundByTheInvoker = Core.InvokerBinding.IsBound(p)
            })
            .ToImmutableArray();
    }

    /// <summary>
    ///     The initializer written on a property, as syntax, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    ///     Public because the endpoint body DTO needs the same answer. A body DTO whose properties had
    ///     no initializer would hand an action declaring <c>public string Slot { get; init; } = ""</c>
    ///     a <c>null</c> whenever the caller omitted the field — a non-nullable property, a documented
    ///     default, and a NullReferenceException inside the action's own code, while the boundary
    ///     overload honoured the default. One rule, one place.
    /// </remarks>
    public static string? ExtractDefaultValueSyntax(IPropertySymbol prop, Compilation compilation)
    {
        foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax { Initializer.Value: { } value })
                continue;

            // Enum defaults are copied verbatim into the boundary overload, which lives in a
            // different namespace. An unqualified member name (e.g. "Priority.Standard") would
            // fail with CS0103, so qualify it with the global enum type name to match the param type.
            // Before the constant fold below, deliberately: an enum member's constant value is its
            // underlying integer, and an int does not convert to an enum without a cast.
            if (GetUnderlyingEnum(prop.Type) is { } enumType)
            {
                var memberName = GetEnumMemberName(value);
                if (memberName is not null)
                {
                    var enumFqn = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return $"{enumFqn}.{memberName}";
                }
            }

            var model = ModelFor(compilation, value.SyntaxTree);
            if (model is null)
                return value.ToString().Trim();

            // What the generator can compute, it computes. nameof(Tone.Bold) is the string "Bold",
            // and the value carries no dependency on a name the generated file cannot bind — which
            // the spelling did, producing a CS0103 inside a file the author cannot open.
            var constant = model.GetConstantValue(value);
            if (constant.HasValue)
                return FormatConstant(constant.Value, prop.Type);

            // Not a constant, so the expression itself has to travel: new List<Tone>(), []. Every
            // type name in it is qualified, for the same reason the enum member above is.
            return new InitializerRewriter(model).Visit(value)!.ToString().Trim();
        }

        return null;
    }

    /// <summary>A C# literal for a folded constant.</summary>
    /// <remarks>
    ///     ⚠️ The <c>!</c> on a null is not decoration. An author writing <c>= null!</c> on a
    ///     non-nullable reference property is suppressing the warning deliberately — "nothing until it
    ///     is bound" — and folding the expression to a bare <c>null</c> drops the suppression along
    ///     with the spelling, which is a <c>CS8625</c> in the generated parameter list.
    /// </remarks>
    private static string FormatConstant(object? value, ITypeSymbol target)
    {
        if (value is not null)
            return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);

        return target is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.NotAnnotated }
            ? "null!"
            : "null";
    }

    /// <summary>
    ///     The semantic model for a tree, or <c>null</c> when the tree does not belong to this
    ///     compilation.
    /// </summary>
    /// <remarks>
    ///     A partial declaration can be split across compilations in principle; asking for a model on
    ///     a foreign tree throws rather than returning null, and an exception here would take the whole
    ///     generator output with it.
    /// </remarks>
    private static SemanticModel? ModelFor(Compilation compilation, SyntaxTree tree)
        => compilation.ContainsSyntaxTree(tree) ? compilation.GetSemanticModel(tree) : null;

    /// <summary>
    ///     Whether a property's initialiser can also serve as a parameter default.
    /// </summary>
    /// <remarks>
    ///     The same syntax feeds two generated shapes. On the request body record it is a property
    ///     initialiser and any expression will do; in the boundary overload it sits after <c>=</c> in
    ///     a parameter list, where C# demands a compile-time constant. <c>= []</c> is the ordinary way
    ///     to write "starts empty" and broke the build with CS1736 there.
    /// </remarks>
    public static bool HasConstantInitializer(IPropertySymbol prop, Compilation compilation)
    {
        foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax { Initializer.Value: { } value })
                continue;

            // An enum member is rewritten to a fully qualified name above, and that is a constant.
            if (GetUnderlyingEnum(prop.Type) is not null)
                return true;

            // The compiler's own answer, where it is available: the extraction folds a constant into
            // a literal, and a literal is exactly what a parameter default accepts. The syntactic
            // check below stays as the fallback for a tree this compilation does not own.
            var model = ModelFor(compilation, value.SyntaxTree);
            return model is not null
                ? model.GetConstantValue(value).HasValue
                : IsConstantExpression(value);
        }

        return false;
    }

    private static bool IsConstantExpression(ExpressionSyntax value) => value switch
    {
        LiteralExpressionSyntax => true,
        DefaultExpressionSyntax => true,
        // -1, +2: a sign in front of a literal is still a constant.
        PrefixUnaryExpressionSyntax unary => IsConstantExpression(unary.Operand),
        // new(), new List<string>(), [], SomeHelper.Value, nameof(x): none of these can be a
        // parameter default, and the ones that could be constants cannot be told apart from here.
        _ => false
    };

    private static INamedTypeSymbol? GetUnderlyingEnum(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return null;

        if (named.EnumUnderlyingType is not null)
            return named;

        // Nullable<TEnum> → unwrap to TEnum.
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
            && named.TypeArguments[0] is INamedTypeSymbol { EnumUnderlyingType: not null } inner)
            return inner;

        return null;
    }

    private static string? GetEnumMemberName(ExpressionSyntax value) => value switch
    {
        // e.g. DeliveryPriority.Standard → "Standard"
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        // e.g. Standard (member referenced directly inside the enum's own type) → "Standard"
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null
    };

    private static bool IsNullableType(ITypeSymbol type)
    {
        return type.NullableAnnotation == NullableAnnotation.Annotated
               || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }
}
