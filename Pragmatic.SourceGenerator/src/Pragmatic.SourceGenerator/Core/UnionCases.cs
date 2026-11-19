using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     A C# 15 union, read by its shape: a struct that implements <c>System.Runtime.CompilerServices.IUnion</c>, whose cases are the parameter
///     types of its one-parameter constructors (docs/design/csharp15-union-errors.md, option A).
/// </summary>
/// <remarks>
///     The generator is built on Roslyn 4.14, which has no union API, and the union syntax of Roslyn 5.x is experimental. A union in source and
///     a union in a referenced assembly have this same shape, so one reading serves both. The Result feature reads a union error set's
///     <c>IError</c> through it (#865), the endpoints its cases (#866).
/// </remarks>
internal static class UnionCases
{
    /// <summary>The interface the compiler puts on every union.</summary>
    public const string UnionInterface = "System.Runtime.CompilerServices.IUnion";

    /// <summary>Whether the type is a union.</summary>
    public static bool IsUnion(ITypeSymbol type)
        => type.TypeKind == TypeKind.Struct && type.AllInterfaces.Any(i => i.ToDisplayString() == UnionInterface);

    /// <summary>The union's cases, each once, in the order of their constructors; empty when the type is not a union.</summary>
    public static ImmutableArray<ITypeSymbol> Of(ITypeSymbol type)
        => type is INamedTypeSymbol named && IsUnion(type)
            ? named.InstanceConstructors
                .Where(c => c.Parameters.Length == 1)
                .Select(c => c.Parameters[0].Type)
                .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default)
                .ToImmutableArray()
            : ImmutableArray<ITypeSymbol>.Empty;
}
