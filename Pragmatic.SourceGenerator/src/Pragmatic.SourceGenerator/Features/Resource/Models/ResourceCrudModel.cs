using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Models;

/// <summary>
/// Model for auto-CRUD generation on a [Resource(Capabilities=...)] entity.
/// Contains entity properties filtered for each DTO type.
/// </summary>
internal sealed record ResourceCrudModel
{
    public required ResourceModel Resource { get; init; }

    /// <summary>All scalar properties (no navigations, no collections).</summary>
    public required EquatableArray<ResourcePropertyInfo> Properties { get; init; }

    /// <summary>Logic key property name (from [GeneratedValue] or [LogicKey]). Null if none.</summary>
    public string? LogicKeyName { get; init; }

    /// <summary>Logic key property type.</summary>
    public string? LogicKeyType { get; init; }

    /// <summary>
    ///     Whether the entity declares <c>[SoftDelete]</c>. Gates the Restore capability: there is
    ///     nothing to put back once a row is physically gone.
    /// </summary>
    public bool IsSoftDelete { get; init; }

    /// <summary>Whether the entity declares <c>[ConcurrencyAware]</c>.</summary>
    public bool IsConcurrencyAware { get; init; }

    /// <summary>
    ///     The group the entity's hand-written operations are in, or null when it has none.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Carried, not inferred: the scaffolded operations take the entity's namespace, which is
    ///     flat by rule, so there is no segment for the group inference to read. Without this they
    ///     landed on the boundary root while their hand-written neighbours were grouped — a seam in the
    ///     facade that exists only because one side was generated.
    /// </remarks>
    public string? GroupName { get; init; }

    /// <summary>
    ///     Whether a write on this resource can be refused by the database as a conflict: a logic key
    ///     behind a unique index, or a concurrency token guarding the row.
    /// </summary>
    /// <remarks>
    ///     Drives the 409 the generated endpoints declare. Measured before being declared — a duplicate
    ///     logic key answers 409 today, and the OpenAPI document listed 400, 401, 403 and 500.
    /// </remarks>
    public bool CanConflict => IsConcurrencyAware || LogicKeyName is not null;

    /// <summary>Properties for CreateDto: required, no PK, no audit, no ownership.</summary>
    public EquatableArray<ResourcePropertyInfo> CreateProperties =>
        Properties.Where(p => p.IncludeInCreate).ToImmutableArray();

    /// <summary>Properties for ReadDto: all including PK and audit.</summary>
    public EquatableArray<ResourcePropertyInfo> ReadProperties => Properties;

    /// <summary>Properties for UpdateDto: all non-audit, nullable (patch semantics).</summary>
    public EquatableArray<ResourcePropertyInfo> UpdateProperties =>
        Properties.Where(p => p.IncludeInUpdate).ToImmutableArray();

    /// <summary>Properties for ListItemDto: PK + key scalars.</summary>
    public EquatableArray<ResourcePropertyInfo> ListProperties =>
        Properties.Where(p => p.IncludeInList).ToImmutableArray();

    /// <summary>
    ///     What the developer declared on the partial parts of this resource's operations.
    /// </summary>
    public EquatableArray<ResourceOverrideModel> Decorations { get; init; } =
        EquatableArray<ResourceOverrideModel>.Empty;

    /// <summary>What the developer declared on one operation, if anything.</summary>
    public ResourceOverrideModel? DecorationOf(string operationTypeName)
    {
        var key = KeyOf(operationTypeName);
        foreach (var decoration in Decorations)
        {
            if (decoration.Key == key)
                return decoration;
        }

        return null;
    }

    /// <summary>Namespace + name, the identity a decoration is matched by.</summary>
    public string KeyOf(string operationTypeName)
        => string.IsNullOrEmpty(Resource.Namespace)
            ? operationTypeName
            : $"{Resource.Namespace}.{operationTypeName}";

    /// <summary>
    ///     The DTO an operation answers with: the developer's if they declared one, otherwise the one
    ///     this generator scaffolds.
    /// </summary>
    /// <param name="operationTypeName">The operation's name, without namespace.</param>
    /// <param name="scaffoldedTypeName">The DTO scaffolded for its shape — Read or ListItem.</param>
    public ResourceDtoRef DtoFor(string operationTypeName, string scaffoldedTypeName)
    {
        if (DecorationOf(operationTypeName)?.DeclaredDto is { } declared)
        {
            return new ResourceDtoRef
            {
                FullTypeName = declared.DtoFullTypeName,
                TypeName = declared.DtoTypeName,
                IsScaffolded = false,
            };
        }

        return new ResourceDtoRef
        {
            FullTypeName = KeyOf(scaffoldedTypeName),
            TypeName = scaffoldedTypeName,
            IsScaffolded = true,
        };
    }

    /// <summary>
    ///     The filters the scaffolded search exposes: what the developer declared on its partial part
    ///     if they declared anything, otherwise the convention.
    /// </summary>
    /// <remarks>
    ///     The convention is every text column that is not identity, ownership, soft-delete or audit
    ///     metadata, matched with <c>Contains</c>. Nullable text — an optional description, a phone
    ///     number — is included: an optional field is exactly the kind of thing people search on, and
    ///     leaving it out made the capability silently partial.
    /// </remarks>
    public EquatableArray<QueryPropertyModel> SearchFilters
    {
        get
        {
            if (DeclaredSearchFilters is { IsDefaultOrEmpty: false } declared)
                return declared;

            var conventional = ImmutableArray.CreateBuilder<QueryPropertyModel>();
            foreach (var property in Properties)
            {
                if (property.TypeName.TrimEnd('?') != "string"
                    || property.IsPrimaryKey || property.IsAudit
                    || property.IsOwnership || property.IsSoftDelete)
                    continue;

                conventional.Add(new QueryPropertyModel
                {
                    PropertyName = property.Name,
                    PropertyType = "string?",
                    IsNullable = true,
                    IsFilter = true,
                    Operator = FilterOperatorKind.Contains,
                    // The column behind an optional text field is nullable, so the generated predicate
                    // has to null-guard it: e.Phone.Contains(v) throws the moment the expression is
                    // evaluated over objects instead of being translated to SQL.
                    EntityPathIsNullable = property.IsNullable,
                });
            }

            return conventional.ToImmutable();
        }
    }

    /// <summary>
    ///     The filters the developer declared on the search's partial part, if they declared any.
    /// </summary>
    public EquatableArray<QueryPropertyModel>? DeclaredSearchFilters
        => DecorationOf($"ResourceSearch{Resource.TypeName}Query")?.DeclaredFilters;

    /// <summary>The name of the scaffolded read shape.</summary>
    public string ScaffoldedReadDto => $"{Resource.TypeName}ReadDto";

    /// <summary>The name of the scaffolded list shape.</summary>
    public string ScaffoldedListItemDto => $"{Resource.TypeName}ListItemDto";

    /// <summary>
    ///     Whether the scaffolded read DTO is still answered with by something, and therefore still
    ///     worth writing. Declaring one's own DTO on every read operation makes it dead code.
    /// </summary>
    public bool NeedsScaffoldedReadDto => ReadShapeOperations.Any(op => DtoFor(op, ScaffoldedReadDto).IsScaffolded);

    /// <summary>Whether the scaffolded list DTO is still answered with by something.</summary>
    public bool NeedsScaffoldedListItemDto =>
        ListShapeOperations.Any(op => DtoFor(op, ScaffoldedListItemDto).IsScaffolded);

    /// <summary>The operations that answer with the read shape, given the capabilities in force.</summary>
    public IEnumerable<string> ReadShapeOperations
    {
        get
        {
            var entity = Resource.TypeName;
            var caps = Resource.Capabilities;

            if ((caps & CapabilityRead) != 0)
            {
                yield return $"ResourceRead{entity}Query";
                if (LogicKeyName is not null)
                    yield return $"ResourceReadBy{LogicKeyName}{entity}Query";
            }

            // The writes answer with the read shape too, whenever the resource still exists after the
            // operation. A create answers with what it created; an update and a restore with what they
            // left behind; a soft delete with the row it marked. Only a hard delete has nothing to say.
            if ((caps & CapabilityCreate) != 0)
                yield return $"ResourceCreate{entity}Mutation";
            if ((caps & CapabilityUpdate) != 0)
                yield return $"ResourceUpdate{entity}Mutation";
            if ((caps & CapabilityDelete) != 0 && IsSoftDelete)
                yield return $"ResourceDelete{entity}Mutation";
            if ((caps & CapabilityRestore) != 0 && IsSoftDelete)
                yield return $"ResourceRestore{entity}Mutation";
        }
    }

    /// <summary>The operations that answer with the list shape, given the capabilities in force.</summary>
    public IEnumerable<string> ListShapeOperations
    {
        get
        {
            var entity = Resource.TypeName;
            var caps = Resource.Capabilities;

            if ((caps & CapabilityList) != 0)
                yield return $"ResourceList{entity}Query";
            if ((caps & CapabilitySearch) != 0)
                yield return $"ResourceSearch{entity}Query";
        }
    }

    /// <summary>
    ///     Every operation scaffolded for this resource, by type name — the ones that answer with a
    ///     shape and the ones that only change something.
    /// </summary>
    public IEnumerable<string> Operations
    {
        get
        {
            var entity = Resource.TypeName;
            var caps = Resource.Capabilities;

            foreach (var operation in ReadShapeOperations)
                yield return operation;
            foreach (var operation in ListShapeOperations)
                yield return operation;

            // The only write not already named above: a hard delete answers with nothing, so it has no
            // shape to declare — but it still has a permission, and can still be decorated.
            if ((caps & CapabilityDelete) != 0 && !IsSoftDelete)
                yield return $"ResourceDelete{entity}Mutation";
        }
    }

    private const int CapabilityCreate = 1;
    private const int CapabilityRead = 2;
    private const int CapabilityUpdate = 4;
    private const int CapabilityDelete = 8;
    private const int CapabilityList = 16;
    private const int CapabilitySearch = 32;
    private const int CapabilityRestore = 64;
}

/// <summary>
/// Metadata for a single entity property, used to generate DTO records.
/// </summary>
internal sealed record ResourcePropertyInfo
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public bool IsNullable { get; init; }
    public bool IsRequired { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsAudit { get; init; }
    public bool IsOwnership { get; init; }
    public bool IsSoftDelete { get; init; }
    public bool IsNavigation { get; init; }

    /// <summary>Include in CreateDto: not PK, not audit, not soft-delete, not navigation.</summary>
    public bool IncludeInCreate => !IsPrimaryKey && !IsAudit && !IsSoftDelete && !IsNavigation && !IsOwnership;

    /// <summary>Include in UpdateDto: not PK, not audit, not soft-delete, not ownership.</summary>
    public bool IncludeInUpdate => !IsPrimaryKey && !IsAudit && !IsSoftDelete && !IsNavigation && !IsOwnership;

    /// <summary>Include in ListItemDto: PK, logic keys, or key scalars (not audit, not navigation).</summary>
    public bool IncludeInList => (IsPrimaryKey || IsRequired) && !IsAudit && !IsNavigation;
}
