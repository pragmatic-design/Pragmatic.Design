using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     One <c>{Database}Schema</c> per database of the host, for Pragmatic.Migrations: the tables of its EF Core entities
///     (<see cref="DbContextFeature" />) and the ones another engine contributes to it (the Sql engine's, <see cref="EngineSchemaModel" />),
///     written by <see cref="SchemaMetadataTemplate" />. A database is one schema whichever engines persist its boundaries: two would be two
///     types of one name, and each a migration that drops the other's tables.
/// </summary>
internal static class SchemaFeature
{
    /// <param name="context">The generator's context.</param>
    /// <param name="efCoreSchemas">The schema of each database's EF Core entities.</param>
    /// <param name="engineSchemas">The tables each other engine contributes, per database.</param>
    public static void Register(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<SchemaMetadataModel>> efCoreSchemas,
        IncrementalValueProvider<EquatableArray<EngineSchemaModel>> engineSchemas)
        => context.RegisterSourceOutputSafe(efCoreSchemas.Combine(engineSchemas), static (ctx, pair) =>
        {
            foreach (var schema in Merge(pair.Left, pair.Right))
            {
                var artifact = new SchemaMetadataTemplate(schema).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }
        });

    /// <summary>
    ///     Each database once: its EF Core schema with the engines' tables added (a table EF Core already describes keeps EF Core's form), or,
    ///     with no EF Core entity in it, the engines' tables in the namespace and provider they were typed for.
    /// </summary>
    internal static List<SchemaMetadataModel> Merge(EquatableArray<SchemaMetadataModel> efCoreSchemas, EquatableArray<EngineSchemaModel> engineSchemas)
    {
        var merged = new List<SchemaMetadataModel>();
        var contributions = engineSchemas.GroupBy(e => e.DatabaseName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var efCore in efCoreSchemas)
        {
            if (!contributions.TryGetValue(efCore.DatabaseName, out var engines))
            {
                merged.Add(efCore);
                continue;
            }

            contributions.Remove(efCore.DatabaseName);
            merged.Add(efCore with
            {
                Tables = efCore.Tables.Concat(Tables(engines, efCore.Tables.Select(t => t.TableName))).ToEquatableArray(),
                ConfigKey = efCore.ConfigKey ?? engines.Select(e => e.ConfigKey).FirstOrDefault(k => k is not null),
            });
        }

        foreach (var engines in contributions.Values)
        {
            var first = engines[0];
            merged.Add(new SchemaMetadataModel(first.DatabaseName, first.Namespace, first.ProviderName, Tables(engines, []).ToEquatableArray(),
                engines.Select(e => e.ConfigKey).FirstOrDefault(k => k is not null)));
        }

        return merged;
    }

    /// <summary>The engines' tables a database does not already have, each once.</summary>
    private static IEnumerable<TableSchemaModel> Tables(List<EngineSchemaModel> engines, IEnumerable<string> existing)
    {
        var seen = new HashSet<string>(existing, StringComparer.Ordinal);
        foreach (var table in engines.SelectMany(e => e.Tables))
        {
            if (seen.Add(table.TableName))
                yield return table;
        }
    }
}
