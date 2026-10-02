using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Reads the set of boundary marker types carrying <c>[EnableBatchProgress]</c>. Boundaries live in
///     referenced assemblies in host mode, so the attribute is read from metadata (via
///     <see cref="Compilation.GetTypeByMetadataName"/>) rather than <c>ForAttributeWithMetadataName</c>.
///     Mirrors <see cref="SagaPersistenceBoundaryReader"/> and <see cref="MessagingOutboxBoundaryReader"/>.
/// </summary>
internal static class BatchProgressBoundaryReader
{
    private const string AttributeName = "EnableBatchProgressAttribute";
    private const string AttributeNamespace = "Pragmatic.Messaging.Attributes";

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
