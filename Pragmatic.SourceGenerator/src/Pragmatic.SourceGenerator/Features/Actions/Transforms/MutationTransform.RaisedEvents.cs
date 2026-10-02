using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class MutationTransform
{
    private const string RaisesAttributeName = "RaisesAttribute";
    private const string RaisesAttributeNamespace = "Pragmatic.Authoring";

    /// <summary>
    ///     Reads <c>[Raises&lt;TEvent&gt;]</c> off the mutation and resolves each event's constructor by name. The
    ///     candidate members are the entity's properties (<c>entity.{Name}</c>, plus the Id conventions) overlaid
    ///     by the mutation's inputs (<c>mutation.{Name}</c>) — so an event param like <c>Amount</c> binds to the
    ///     input while <c>InvoiceId</c> binds to <c>entity.Id</c>. The generated invoker raises these on the entity.
    /// </summary>
    private static ImmutableArray<RaisedEventModel> ParseRaisedEvents(
        INamedTypeSymbol symbol,
        ImmutableArray<ActionPropertyModel> inputProperties,
        INamedTypeSymbol entityType)
    {
        var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = "entity.Id",
            ["PersistenceId"] = "entity.Id",
            [entityType.Name + "Id"] = "entity.Id"
        };

        foreach (var p in entityType.GetMembers().OfType<IPropertySymbol>())
            if (!p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
                members[p.Name] = $"entity.{p.Name}";

        // And the members other generators will add — trait members and the foreign keys of
        // [Relation.*] — which are not on the symbol during this pass. Without them an event parameter
        // named after a generated key matched nothing and was passed `default`.
        foreach (var generated in Core.TraitPropertyResolver.GetGeneratedProperties(entityType))
            if (!members.ContainsKey(generated.Name))
                members[generated.Name] = $"entity.{generated.Name}";

        // The mutation's inputs are the operation's intent; let them win on a name collision.
        foreach (var ip in inputProperties)
            members[ip.Name] = $"mutation.{ip.Name}";

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
