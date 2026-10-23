using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The engines, as <c>Pragmatic.Persistence.Entity.PersistenceEngineKind</c> numbers them.</summary>
internal enum PragmaticEngine
{
    EfCore = 0,
    Sql = 1,
    Documents = 2,
}

/// <summary>A boundary choice that cannot stand: no engine where several are referenced, or one that is not referenced.</summary>
internal sealed class EngineProblem(INamedTypeSymbol entity, ITypeSymbol? boundary, PragmaticEngine? named, string referenced)
{
    public INamedTypeSymbol Entity { get; } = entity;
    public ITypeSymbol? Boundary { get; } = boundary;

    /// <summary>The engine the boundary names, when it names one that is not referenced; null when it names none.</summary>
    public PragmaticEngine? Named { get; } = named;

    /// <summary>The engines referenced, for the message.</summary>
    public string Referenced { get; } = referenced;

    public Location Location => (Named is null ? Entity.Locations.FirstOrDefault() : Boundary?.Locations.FirstOrDefault()) ?? Location.None;
    public string BoundaryText => Boundary?.ToDisplayString() ?? "(none)";
}

/// <summary>
///     Which engine persists each of Pragmatic's entities. The entity's boundary (<see cref="PragmaticPermissions.BoundaryOf" />) names it with
///     <c>[PersistenceEngine]</c>; without the marker, the only engine the compilation references is the default. An engine is referenced when
///     its bridge's type is there: <c>PragmaticDbContextAttribute</c> for EF Core, <c>PragmaticSqlFilters</c> for the Sql engine,
///     <c>PragmaticDocFilters</c> for the documents engine. With none referenced (a generator's own tests), the generator that runs serves
///     every unmarked entity, as before the marker. Each generator emits for the entities it serves only, so no two engines intercept one
///     <c>Spec&lt;T&gt;.Where</c>.
/// </summary>
internal sealed class PragmaticEngines
{
    private const string EntityAttribute = "Pragmatic.Persistence.Entity.EntityAttribute";

    private static readonly (PragmaticEngine Engine, string Probe)[] Probes =
    [
        (PragmaticEngine.EfCore, "Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute"),
        (PragmaticEngine.Sql, "Pragmatic.Persistence.Sql.Pragmatic.PragmaticSqlFilters"),
        (PragmaticEngine.Documents, "Pragmatic.Persistence.Documents.Pragmatic.PragmaticDocFilters"),
    ];

    private static readonly ConditionalWeakTable<Compilation, PragmaticEngines> Cache = new();

    private readonly List<PragmaticEngine> _referenced;
    private readonly Dictionary<INamedTypeSymbol, PragmaticEngine?> _engines = new(SymbolEqualityComparer.Default);

    private PragmaticEngines(Compilation compilation)
    {
        _referenced = Probes.Where(p => compilation.GetTypeByMetadataName(p.Probe) is not null).Select(p => p.Engine).ToList();
        var referenced = _referenced.Count == 0 ? "none" : string.Join(", ", _referenced);
        if (compilation.GetTypeByMetadataName(EntityAttribute) is not { } attribute)
            return;
        foreach (var entity in PragmaticNames.AllTypes(compilation.GlobalNamespace)
                     .Where(t => t.DeclaringSyntaxReferences.Length > 0 && t.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute))))
        {
            var boundary = PragmaticPermissions.BoundaryOf(entity);
            var named = boundary is null ? null : MarkedEngine(boundary);
            if (named is { } engine)
            {
                _engines[entity] = engine;
                if (_referenced.Count > 0 && !_referenced.Contains(engine))
                    Problems.Add(new EngineProblem(entity, boundary, engine, referenced));
            }
            else if (_referenced.Count >= 2)
            {
                _engines[entity] = null;
                Problems.Add(new EngineProblem(entity, boundary, null, referenced));
            }
            else
            {
                _engines[entity] = _referenced.Count == 1 ? _referenced[0] : null;
            }
        }
    }

    /// <summary>The choices that cannot stand, one per entity; reported by one generator (<see cref="Reports" />).</summary>
    public List<EngineProblem> Problems { get; } = [];

    public static PragmaticEngines Of(Compilation compilation) => Cache.GetValue(compilation, c => new PragmaticEngines(c));

    /// <summary>
    ///     True when <paramref name="engine" />'s generator emits for <paramref name="entity" />: the engine its boundary names, else the only one
    ///     referenced, else (none referenced) any. An entity declared elsewhere (a relation's target in a referenced assembly) is read the same way.
    /// </summary>
    public bool Serves(ITypeSymbol? entity, PragmaticEngine engine)
    {
        if (entity is not INamedTypeSymbol named)
            return false;
        if (!_engines.TryGetValue(named.OriginalDefinition, out var chosen))
        {
            var boundary = PragmaticPermissions.BoundaryOf(named);
            chosen = (boundary is null ? null : MarkedEngine(boundary)) ?? (_referenced.Count == 1 ? _referenced[0] : null);
        }

        return chosen is null ? _referenced.Count == 0 : chosen == engine;
    }

    /// <summary>The generator that reports <see cref="Problems" />, so they are reported once: the Sql engine's when referenced, else the documents engine's.</summary>
    public bool Reports(PragmaticEngine engine)
        => engine == (_referenced.Contains(PragmaticEngine.Sql) ? PragmaticEngine.Sql : _referenced.Contains(PragmaticEngine.Documents) ? PragmaticEngine.Documents : engine);

    // The attribute is decoded once, for the whole generator (PersistenceEngineMarker): this feature's own copy of its name had kept the
    // spike's namespace, and saw no boundary name an engine.
    private static PragmaticEngine? MarkedEngine(ITypeSymbol boundary)
        => boundary is INamedTypeSymbol named && PersistenceEngineMarker.NamedOn(named) is { } engine
            && Enum.TryParse<PragmaticEngine>(engine, out var value) ? value : null;
}
