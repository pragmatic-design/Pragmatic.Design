using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    private const string RaisesAttributeName = "RaisesAttribute";
    private const string RaisesAttributeNamespace = "Pragmatic.Authoring";

    /// <summary>
    ///     Reads <c>[Raises&lt;TEvent&gt;]</c> off the action and resolves each event's constructor by matching
    ///     parameter names to the action's input properties (<c>action.{Name}</c>). The generated invoker then
    ///     constructs and dispatches these after a successful commit — behavior lives in the operation, not the
    ///     entity (#4 / anemic-operations model).
    /// </summary>
    private static ImmutableArray<RaisedEventModel> ParseRaisedEvents(
        INamedTypeSymbol symbol,
        ImmutableArray<ActionPropertyModel> inputProperties)
    {
        var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in inputProperties)
            members[p.Name] = $"action.{p.Name}";

        var builder = ImmutableArray.CreateBuilder<RaisedEventModel>();
        foreach (var attr in symbol.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null ||
                ac.Name != RaisesAttributeName ||
                !ac.IsGenericType ||
                ac.TypeArguments.Length != 1 ||
                ac.ContainingNamespace?.ToDisplayString() != RaisesAttributeNamespace)
                continue;

            if (ac.TypeArguments[0] is not INamedTypeSymbol eventType)
                continue;

            var resolved = EventConstructorMatcher.ResolveArguments(eventType, members);

            builder.Add(new RaisedEventModel
            {
                EventFullName = eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ConstructorArguments = resolved.Arguments,
                UnmatchedParameters = resolved.Unmatched
            });
        }

        return builder.ToImmutable();
    }
}
