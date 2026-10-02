using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Reads the set of boundary marker types carrying <c>[EnableJobPersistence]</c> — the boundary whose
///     generated DbContext hosts <c>__Jobs</c> and <c>__RecurringJobs</c>. Boundaries live in referenced
///     assemblies in host mode, so the attribute is read from metadata rather than with
///     <c>ForAttributeWithMetadataName</c>. Mirrors <see cref="BatchProgressBoundaryReader" />.
/// </summary>
internal static class JobPersistenceBoundaryReader
{
    private const string AttributeName = "EnableJobPersistenceAttribute";
    private const string AttributeNamespace = "Pragmatic.Jobs.Attributes";

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
