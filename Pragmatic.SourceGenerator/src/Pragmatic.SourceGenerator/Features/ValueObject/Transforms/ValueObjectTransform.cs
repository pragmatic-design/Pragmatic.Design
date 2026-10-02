using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.ValueObject.Models;

namespace Pragmatic.SourceGenerator.Features.ValueObject.Transforms;

internal static class ValueObjectTransform
{
    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;

    public static ValueObjectModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var isPartial = typeSymbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .OfType<TypeDeclarationSyntax>()
            .Any(t => t.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

        var typeKindKeyword = typeSymbol.IsValueType ? "record struct" : "record";

        // Validate: a static method named Validate. Its signature is mirrored by Create.
        var validate = typeSymbol.GetMembers("Validate")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsStatic);

        var hasValidate = validate is not null;
        var validateReturn = validate?.ReturnType.ToDisplayString(FullyQualified);
        var validateParams = validate is null
            ? ImmutableArray<ValueObjectParameter>.Empty
            : validate.Parameters
                .Select(p => new ValueObjectParameter(p.Type.ToDisplayString(FullyQualified), p.Name))
                .ToImmutableArray();

        // Constructor for CreateUnsafe: prefer the one matching Validate's parameter types,
        // else the accessible constructor with the most parameters. Exclude the synthesized
        // record copy-constructor (single parameter of the same type).
        var candidateCtors = typeSymbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .Where(c => !IsCopyConstructor(c, typeSymbol))
            .ToArray();

        IMethodSymbol? chosenCtor = null;
        if (validate is not null)
            chosenCtor = candidateCtors.FirstOrDefault(c => ParametersMatch(c, validate));
        chosenCtor ??= candidateCtors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        var hasConstructor = chosenCtor is { Parameters.Length: > 0 };
        var ctorParams = !hasConstructor
            ? ImmutableArray<ValueObjectParameter>.Empty
            : chosenCtor!.Parameters
                .Select(p => new ValueObjectParameter(p.Type.ToDisplayString(FullyQualified), p.Name))
                .ToImmutableArray();

        var userCreate = typeSymbol.GetMembers("Create").OfType<IMethodSymbol>().Any(m => m.IsStatic);
        var userCreateUnsafe = typeSymbol.GetMembers("CreateUnsafe").OfType<IMethodSymbol>().Any(m => m.IsStatic);

        return new ValueObjectModel
        {
            Namespace = ns,
            TypeName = typeSymbol.Name,
            Accessibility = AccessibilityKeyword(typeSymbol.DeclaredAccessibility),
            TypeKindKeyword = typeKindKeyword,
            IsPartial = isPartial,
            HasValidate = hasValidate,
            ValidateReturnType = validateReturn,
            ValidateParameters = validateParams,
            HasConstructor = hasConstructor,
            ConstructorParameters = ctorParams,
            UserDefinedCreate = userCreate,
            UserDefinedCreateUnsafe = userCreateUnsafe,
        };
    }

    private static bool IsCopyConstructor(IMethodSymbol ctor, INamedTypeSymbol typeSymbol)
        => ctor.Parameters.Length == 1
           && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, typeSymbol);

    private static bool ParametersMatch(IMethodSymbol ctor, IMethodSymbol validate)
    {
        if (ctor.Parameters.Length != validate.Parameters.Length)
            return false;
        for (var i = 0; i < ctor.Parameters.Length; i++)
            if (!SymbolEqualityComparer.Default.Equals(ctor.Parameters[i].Type, validate.Parameters[i].Type))
                return false;
        return true;
    }

    private static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "internal",
    };
}
