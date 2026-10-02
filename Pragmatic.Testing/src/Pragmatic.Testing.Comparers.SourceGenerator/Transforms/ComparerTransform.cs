using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Comparers.SourceGenerator.Models;

namespace Pragmatic.Testing.Comparers.SourceGenerator.Transforms;

/// <summary>Turns the type named by <c>[GenerateComparer&lt;T&gt;]</c> into a <see cref="ComparerModel"/>.</summary>
internal static class ComparerTransform
{
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    ///     Builds the model, or returns null when the type has no readable public members — a
    ///     comparer for it would report every pair as equivalent, which is worse than not having one.
    /// </summary>
    internal static ComparerModel? Build(INamedTypeSymbol type)
    {
        var members = new List<ComparedMemberModel>();

        // The type's own members first, then those it inherits — the order a reader expects to see
        // them reported in.
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
        {
            if (member is { DeclaredAccessibility: not Accessibility.Public } or { GetMethod: null }
                || member.Parameters.Length > 0
                || member.IsStatic
                // A record's compiler-generated EqualityContract is not something a test compares.
                || member.Name == "EqualityContract"
                || members.Any(m => m.Name == member.Name))
                continue;

            members.Add(new ComparedMemberModel(member.Name, IsSequence(member.Type)));
        }

        return members.Count == 0
            ? null
            : new ComparerModel
            {
                TypeFullName = type.ToDisplayString(FullyQualified),
                TypeShortName = type.Name,
                ClassName = type.Name + "Comparer",
                Members = members.ToEquatableArray()
            };
    }

    /// <summary>
    ///     Whether the member holds a sequence, and so must be compared element by element.
    /// </summary>
    /// <remarks>
    ///     A string is an <c>IEnumerable&lt;char&gt;</c> and is deliberately not treated as one:
    ///     comparing it character by character would report "element 3 differs" where the reader
    ///     wants the two strings.
    /// </remarks>
    private static bool IsSequence(ITypeSymbol type) =>
        type.SpecialType != SpecialType.System_String
        && (type.SpecialType == SpecialType.System_Collections_IEnumerable
            || type.AllInterfaces.Any(static i =>
                i.SpecialType == SpecialType.System_Collections_IEnumerable));
}
