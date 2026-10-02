using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Resolves an event's primary-constructor arguments by matching each parameter name (case-insensitive)
///     to a caller-supplied member-access expression — the ctor-by-name convention shared by lifecycle raises
///     and operation auto-raises (#4). <c>OccurredAt</c> is filled from the clock; an unmatched parameter
///     falls back to <c>default</c> with a trailing comment so the gap is visible in the generated source.
/// </summary>
internal static class EventConstructorMatcher
{
    /// <param name="eventType">The domain-event type to construct.</param>
    /// <param name="members">
    ///     Map of candidate member name → access expression (e.g. <c>"DrugId" → "action.DrugId"</c>). Built
    ///     case-insensitively by the caller.
    /// </param>
    public static (ImmutableArray<string> Arguments, ImmutableArray<string> Unmatched) ResolveArguments(
        INamedTypeSymbol eventType,
        IReadOnlyDictionary<string, string> members)
    {
        var ctor = PrimaryConstructor(eventType);
        if (ctor is null)
            return (ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

        var args = ImmutableArray.CreateBuilder<string>(ctor.Parameters.Length);
        var unmatched = ImmutableArray.CreateBuilder<string>();

        foreach (var p in ctor.Parameters)
        {
            if (p.Name.Equals("OccurredAt", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("global::System.DateTimeOffset.UtcNow");
            }
            else if (members.TryGetValue(p.Name, out var access))
            {
                args.Add(access);
            }
            else
            {
                // Still emitted, so the generated file compiles and the author can see the shape — but
                // reported, because default means Guid.Empty or null on an event that is dispatched
                // anyway. A handler reads a blank id and nothing failed.
                args.Add($"default /* unmatched: {p.Name} */");
                unmatched.Add(p.Name);
            }
        }

        return (args.ToImmutable(), unmatched.ToImmutable());
    }

    /// <summary>Primary constructor = the most-parameterful public ctor, excluding the record copy-constructor.</summary>
    private static IMethodSymbol? PrimaryConstructor(INamedTypeSymbol eventType) =>
        eventType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !(c.Parameters.Length == 1 &&
                          SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, eventType)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
}
