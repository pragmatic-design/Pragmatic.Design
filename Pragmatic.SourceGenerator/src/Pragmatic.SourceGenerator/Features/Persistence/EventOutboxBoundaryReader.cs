using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Reads the set of boundary marker types carrying <c>[EnableEventOutbox]</c>. Boundaries live in
///     referenced assemblies in host mode, so the attribute is read from metadata (via
///     <see cref="Compilation.GetTypeByMetadataName"/>) rather than <c>ForAttributeWithMetadataName</c>,
///     which only sees the current compilation's syntax.
/// </summary>
internal static class EventOutboxBoundaryReader
{
    private const string AttributeName = "EnableEventOutboxAttribute";
    private const string AttributeNamespace = "Pragmatic.Events.Attributes";

    public static EquatableArray<string> ReadEnabledBoundaries(
        Compilation compilation,
        ImmutableArray<EntityMetadataModel> entities,
        CancellationToken ct)
    {
        if (entities.IsDefaultOrEmpty)
            return EquatableArray<string>.Empty;

        var boundaryFqns = entities
            .Select(e => e.BoundaryTypeFullName)
            .Where(fqn => !string.IsNullOrEmpty(fqn))
            .Distinct(StringComparer.Ordinal);

        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var fqn in boundaryFqns)
        {
            ct.ThrowIfCancellationRequested();

            var symbol = compilation.GetTypeByMetadataName(fqn!);
            if (symbol is null)
                continue;

            if (symbol.GetAttributes().Any(a =>
                    a.AttributeClass is { Name: AttributeName } cls
                    && cls.ContainingNamespace?.ToDisplayString() == AttributeNamespace))
            {
                result.Add(fqn!);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(result.ToImmutable());
    }
}
