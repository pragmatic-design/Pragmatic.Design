using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Maps boundary types to their database assignments, extracted from [Include&lt;TModule, TDatabase&gt;]
///     declarations on the host [Module] class. Used to generate per-database MigrationDbContexts.
/// </summary>
internal sealed record DatabaseTopologyInfo
{
    /// <summary>
    ///     Mapping from boundary type full name (global::...) to database info.
    ///     Empty when no [Include] declarations are found (single MigrationDbContext fallback).
    /// </summary>
    /// <remarks>
    ///     <see cref="EquatableDictionary{TKey,TValue}" />, not <c>ImmutableDictionary</c>: being a record
    ///     is not enough, because the generated equality delegates to the member's own <c>Equals</c> — and
    ///     an <c>ImmutableDictionary</c> compares by reference. With a non-empty topology every re-read
    ///     produced a new instance, so the record compared unequal and the whole DbContext/schema pipeline
    ///     re-ran on every edit. (Empty hid the bug: the empty singleton IS reference-equal.)
    /// </remarks>
    public EquatableDictionary<string, DatabaseAssignment> BoundaryToDatabase { get; init; } =
        EquatableDictionary<string, DatabaseAssignment>.Empty;

    /// <summary>
    ///     Whether topology information is available (host has [Include&lt;T, TDb&gt;] declarations).
    /// </summary>
    public bool HasTopology => !BoundaryToDatabase.IsEmpty;
}

/// <summary>
///     Represents a database assignment for a boundary module.
/// </summary>
internal sealed record DatabaseAssignment
{
    /// <summary>Fully qualified database type name (e.g., "global::Showcase.Host.ShowcaseAppDatabase").</summary>
    public required string DatabaseTypeName { get; init; }

    /// <summary>Simple class name of the database (e.g., "ShowcaseAppDatabase").</summary>
    public required string DatabaseClassName { get; init; }

    /// <summary>Configuration key for the connection string (e.g., "ConnectionStrings:App").</summary>
    public string? ConfigKey { get; init; }

    /// <summary>
    ///     Position of the <c>[Include&lt;TModule, TDatabase&gt;]</c> that produced this assignment — the
    ///     host declaration a topology diagnostic has to point at. Equality-neutral
    ///     (see <see cref="LocationInfo"/>), so it does not disturb the DbContext/schema caching that
    ///     compares <see cref="DatabaseTopologyInfo"/>.
    /// </summary>
    public LocationInfo? Location { get; init; }
}
