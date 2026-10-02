using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads entity metadata from enriched [PragmaticMetadata(Persistence)] JSON attributes
///     instead of doing a full type scan. Falls back to <see cref="ReadFromReferences"/>
///     for assemblies without enriched metadata (schema &lt; 1.1).
/// </summary>
internal static partial class EntityMetadataReader
{
    private const string MetadataAttributeFullName =
        "Pragmatic.Composition.Attributes.PragmaticMetadataAttribute";

    /// <summary>
    ///     Reads entity metadata from referenced assemblies using enriched JSON metadata when available.
    ///     For assemblies with schema &gt;= 1.1, parses JSON directly (O(entity count)).
    ///     For older assemblies, falls back to full type scan via <see cref="ReadFromReferences"/>.
    /// </summary>
    public static ImmutableArray<EntityMetadataModel> ReadFromPersistenceMetadata(
        Compilation compilation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Only scan references in host mode
        if (compilation.Options.OutputKind is not (OutputKind.ConsoleApplication or OutputKind.WindowsApplication))
            return ImmutableArray<EntityMetadataModel>.Empty;

        var metadataAttrSymbol = compilation.GetTypeByMetadataName(MetadataAttributeFullName);
        var entities = ImmutableArray.CreateBuilder<EntityMetadataModel>();
        var assembliesWithEnrichedMetadata = new HashSet<string>(StringComparer.Ordinal);

        // ⚠️ A package's entities arrive with no boundary — a package does not know which application
        // will import it — and an entity with no boundary reaches no DbContext and no schema, so
        // `Set<T>()` throws at the first call. The importing module said which boundary adopts them on
        // [UsePackage<TPackage, TBoundary>]; the host is the only compilation that sees both.
        var boundaryByPackageAssembly = ReadPackageAdoptions(compilation, ct);

        // Phase 1: Read from enriched JSON metadata (fast path)
        if (metadataAttrSymbol is not null)
        {
            foreach (var reference in compilation.References)
            {
                ct.ThrowIfCancellationRequested();

                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                    continue;

                if (IsFrameworkAssembly(assembly.Name))
                    continue;

                var jsonEntities = TryReadFromAssemblyMetadata(assembly, metadataAttrSymbol, ct);
                if (jsonEntities is not null)
                {
                    assembliesWithEnrichedMetadata.Add(assembly.Name);
                    boundaryByPackageAssembly.TryGetValue(assembly.Name, out var adopting);
                    foreach (var entity in jsonEntities.Value)
                        entities.Add(Adopted(entity, adopting));
                }
            }
        }

        // Phase 2: Fallback to full type scan for assemblies without enriched metadata
        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            if (IsFrameworkAssembly(assembly.Name))
                continue;

            // Skip assemblies already read from JSON
            if (assembliesWithEnrichedMetadata.Contains(assembly.Name))
                continue;

            foreach (var type in GetAllNamedTypes(assembly.GlobalNamespace, ct))
            {
                ct.ThrowIfCancellationRequested();

                var model = EntityTransform.TransformFromSymbol(type, ct);
                if (model is null)
                    continue;

                boundaryByPackageAssembly.TryGetValue(assembly.Name, out var adopting);
                entities.Add(Adopted(model with { IsFromReference = true }, adopting));
            }
        }

        return entities.ToImmutable();
    }

    /// <summary>
    ///     Tries to read entity metadata from assembly-level [PragmaticMetadata(Persistence)] attributes.
    ///     Returns null if the assembly has no enriched persistence metadata (schema &lt; 1.1).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every Persistence entry is read, not the first. <c>PragmaticMetadataAttribute</c> is
    ///         declared <c>AllowMultiple</c>, the generated host calls the registration method of
    ///         every Persistence entry, and <c>MetadataReader.ExtractRepositoryRegistrations</c>
    ///         already aggregates across them — returning on the first would make this the one reader
    ///         of the channel that disagrees with the rest.
    ///     </para>
    ///     <para>
    ///         Reading only the first, a second entry carrying only a registration method, with no
    ///         entities of its own, would make the whole assembly look as though it declared none, and
    ///         its boundaries would lose their generated <c>Add{Boundary}DbContext</c>.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<EntityMetadataModel>? TryReadFromAssemblyMetadata(
        IAssemblySymbol assembly,
        INamedTypeSymbol metadataAttrSymbol,
        CancellationToken ct)
    {
        ImmutableArray<EntityMetadataModel>.Builder? entities = null;

        foreach (var attr in assembly.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, metadataAttrSymbol))
                continue;

            if (attr.ConstructorArguments.Length < 3)
                continue;

            // Check category = Persistence (10)
            var categoryValue = attr.ConstructorArguments[0].Value;
            if (categoryValue is not int categoryInt || categoryInt.ToString() != MetadataCategoryIds.Persistence)
                continue;

            // Check schema version >= 1.1 (enriched metadata)
            var schemaVersion = attr.ConstructorArguments[1].Value as string;
            if (!IsEnrichedSchema(schemaVersion))
                return null; // Has persistence metadata but old schema — fallback to scan

            var jsonData = attr.ConstructorArguments[2].Value as string;
            if (string.IsNullOrEmpty(jsonData))
                continue;

            // An entry is answered even when it carries no entities: it is still enriched metadata,
            // so the assembly must not fall back to a type scan that would rediscover them all.
            entities ??= ImmutableArray.CreateBuilder<EntityMetadataModel>();
            entities.AddRange(ParseEntitiesFromJson(jsonData!, ct));
        }

        return entities?.ToImmutable();
    }

    /// <summary>
    ///     Checks if the schema version supports enriched entity metadata (>= 1.1).
    /// </summary>
    private static bool IsEnrichedSchema(string? version)
    {
        if (string.IsNullOrEmpty(version))
            return false;

        var parts = version!.Split('.');
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;

        // Schema 1.1+ has enriched entity metadata
        return major > 1 || (major == 1 && minor >= 1);
    }

    /// <summary>
    ///     Parses all entities from the enriched persistence metadata JSON.
    /// </summary>
    /// <remarks>
    ///     Internal rather than private so a test can put a document through both ends of this channel
    ///     at once. Everything the host knows about an entity in a referenced assembly arrives here,
    ///     and a fact the writer does not write reads back as a fact the entity does not have — which
    ///     is indistinguishable, in the generated output, from the entity not having it.
    /// </remarks>
    internal static ImmutableArray<EntityMetadataModel> ParseEntitiesFromJson(
        string jsonData,
        CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return ImmutableArray<EntityMetadataModel>.Empty;

            if (!data.TryGetProperty("entities", out var entitiesArray) ||
                entitiesArray.ValueKind != JsonValueKind.Array)
                return ImmutableArray<EntityMetadataModel>.Empty;

            var entities = ImmutableArray.CreateBuilder<EntityMetadataModel>();

            foreach (var element in entitiesArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                var entity = ParseEntityFromJson(element);
                if (entity is not null)
                    entities.Add(entity);
            }

            return entities.ToImmutable();
        }
        catch
        {
            // Malformed JSON — fall back to type scan for this assembly
            return ImmutableArray<EntityMetadataModel>.Empty;
        }
    }

    /// <summary>
    ///     Parses a single EntityMetadataModel from a JSON element.
    /// </summary>
    private static EntityMetadataModel? ParseEntityFromJson(JsonElement element)
    {
        var fullTypeName = GetJsonString(element, "type");
        var idType = GetJsonString(element, "idType");
        var ns = GetJsonString(element, "namespace");
        var typeName = GetJsonString(element, "typeName");

        // These 4 fields are required — if missing, this is old-schema entity
        if (fullTypeName is null || idType is null || ns is null || typeName is null)
            return null;

        var properties = ParsePropertiesFromJson(element);
        var navigations = ParseNavigationsFromJson(element);
        var relationAttributes = ParseRelationAttributesFromJson(element);
        var allSourceMemberNames = ParseStringArray(element, "allSourceMemberNames");
        var ignoredPropertyNames = ParseStringArray(element, "ignoredPropertyNames");

        // Parse temporal
        int temporalMaxActive = 0;
        bool temporalAllowOverlap = false;
        string? temporalParentTypeName = null;
        string? temporalParentTypeFullName = null;
        string? temporalChildTypeName = null;
        string? temporalChildTypeFullName = null;
        string? temporalParentFkProperty = null;
        string? temporalChildFkProperty = null;

        if (element.TryGetProperty("temporal", out var temporal) && temporal.ValueKind == JsonValueKind.Object)
        {
            temporalMaxActive = GetJsonInt(temporal, "maxActive");
            temporalAllowOverlap = GetJsonBool(temporal, "allowOverlap");
            temporalParentTypeName = GetJsonString(temporal, "parentTypeName");
            temporalParentTypeFullName = GetJsonString(temporal, "parentTypeFullName");
            temporalChildTypeName = GetJsonString(temporal, "childTypeName");
            temporalChildTypeFullName = GetJsonString(temporal, "childTypeFullName");
            temporalParentFkProperty = GetJsonString(temporal, "parentFkProperty");
            temporalChildFkProperty = GetJsonString(temporal, "childFkProperty");
        }

        // Parse filters for backward compat (isSoftDelete/isTenantEntity/isTemporalRelation
        // are also available as top-level booleans in schema 1.1+)
        var isSoftDelete = GetJsonBool(element, "isSoftDelete");
        var isTenantEntity = GetJsonBool(element, "isTenantEntity");
        var isTemporalRelation = element.TryGetProperty("temporal", out _) ||
                                 (element.TryGetProperty("filters", out var filters) &&
                                 filters.ValueKind == JsonValueKind.Object &&
                                 GetJsonBool(filters, "temporal"));

        // Top-level booleans override filter-derived values
        if (!isSoftDelete && element.TryGetProperty("filters", out var filterObj) &&
            filterObj.ValueKind == JsonValueKind.Object)
            isSoftDelete = GetJsonBool(filterObj, "softDelete");

        if (!isTenantEntity && element.TryGetProperty("filters", out var filterObj2) &&
            filterObj2.ValueKind == JsonValueKind.Object)
            isTenantEntity = GetJsonBool(filterObj2, "tenant");

        // Ownership/scoping: read from top-level or filters object
        var isOwnedEntity = GetJsonBool(element, "isOwnedEntity");
        var isScopedEntity = GetJsonBool(element, "isScopedEntity");
        if (!isOwnedEntity && element.TryGetProperty("filters", out var filterObj3) &&
            filterObj3.ValueKind == JsonValueKind.Object)
            isOwnedEntity = GetJsonBool(filterObj3, "ownership");
        if (!isScopedEntity && element.TryGetProperty("filters", out var filterObj4) &&
            filterObj4.ValueKind == JsonValueKind.Object)
            isScopedEntity = GetJsonBool(filterObj4, "scoped");

        return new EntityMetadataModel
        {
            VisibilityRules = ReadVisibilityRules(element),
            TypeName = typeName,
            FullTypeName = fullTypeName,
            Namespace = ns,
            IdType = NormalizeIdType(idType),
            Accessibility = GetJsonString(element, "accessibility") ?? "public",
            IsAbstract = GetJsonBool(element, "isAbstract"),
            IsAuditable = GetJsonBool(element, "isAuditable"),
            IsAudited = GetJsonBool(element, "isAudited"),
            IsDataSubject = GetJsonBool(element, "isDataSubject"),
            IsConcurrencyAware = GetJsonBool(element, "isConcurrencyAware"),
            IsSoftDelete = isSoftDelete,
            IsSoftDeleteCascade = GetJsonBool(element, "isSoftDeleteCascade"),
            IsTenantEntity = isTenantEntity,
            IsOwnedEntity = isOwnedEntity,
            ReadAccessTypes = ReadStringArray(element, "readAccessTypes"),
            IsScopedEntity = isScopedEntity,
            IsTemporalRelation = isTemporalRelation,
            IsLookup = GetJsonBool(element, "isLookup"),
            UsesRelationAttributes = GetJsonBool(element, "usesRelationAttributes"),
            BoundaryName = GetJsonString(element, "boundaryName"),
            BoundaryTypeFullName = GetJsonString(element, "boundaryTypeFullName"),
            LogicKeys = ReadLogicKeys(element),
            UniqueIndexes = ReadUniqueIndexes(element),
            CustomTableName = GetJsonString(element, "customTableName"),
            CustomSchemaName = GetJsonString(element, "customSchemaName"),
            InheritanceStrategy = GetJsonString(element, "inheritanceStrategy"),
            DiscriminatorColumn = GetJsonString(element, "discriminatorColumn"),
            BaseEntityFullTypeName = GetJsonString(element, "baseEntityFullTypeName"),
            HasGenerateTimeline = GetJsonBool(element, "hasGenerateTimeline"),
            TemporalMaxActive = temporalMaxActive,
            TemporalAllowOverlap = temporalAllowOverlap,
            TemporalParentTypeName = temporalParentTypeName,
            TemporalParentTypeFullName = temporalParentTypeFullName,
            TemporalChildTypeName = temporalChildTypeName,
            TemporalChildTypeFullName = temporalChildTypeFullName,
            TemporalParentFkProperty = temporalParentFkProperty,
            TemporalChildFkProperty = temporalChildFkProperty,
            HasManualPersistenceId = GetJsonBool(element, "hasManualPersistenceId"),
            HasManualAuditableProps = GetJsonBool(element, "hasManualAuditableProps"),
            HasManualSoftDeleteProps = GetJsonBool(element, "hasManualSoftDeleteProps"),
            HasManualEntityInterface = GetJsonBool(element, "hasManualEntityInterface"),
            HasManualAuditableInterface = GetJsonBool(element, "hasManualAuditableInterface"),
            HasManualSoftDeleteInterface = GetJsonBool(element, "hasManualSoftDeleteInterface"),
            Properties = properties,
            Navigations = navigations,
            RelationAttributes = relationAttributes,
            AllSourceMemberNames = allSourceMemberNames,
            IgnoredPropertyNames = ignoredPropertyNames,
            IsFromReference = true,
            IsValid = true
        };
    }

    // =========================================================================
    // JSON property parsing helpers
    // =========================================================================

    private static ImmutableArray<PropertyMetadataModel> ParsePropertiesFromJson(JsonElement parent)
    {
        if (!parent.TryGetProperty("properties", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ImmutableArray<PropertyMetadataModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<PropertyMetadataModel>();
        foreach (var prop in arr.EnumerateArray())
        {
            var name = GetJsonString(prop, "name");
            var typeName = GetJsonString(prop, "typeName");
            if (name is null || typeName is null)
                continue;

            builder.Add(new PropertyMetadataModel
            {
                Name = name,
                TypeName = typeName,
                HasPrivateSetter = GetJsonBool(prop, "hasPrivateSetter"),
                IsNullable = GetJsonBool(prop, "isNullable"),
                IsEnum = GetJsonBool(prop, "isEnum"),
                IsNavigation = GetJsonBool(prop, "isNavigation"),
                IsLogicKey = GetJsonBool(prop, "isLogicKey"),
                HasDefaultValue = GetJsonBool(prop, "hasDefaultValue"),
                MaxLength = GetJsonIntNullable(prop, "maxLength"),
                Precision = GetJsonIntNullable(prop, "precision"),
                Scale = GetJsonIntNullable(prop, "scale"),
                DefaultValueExpression = GetJsonString(prop, "defaultValueExpression"),
                ComputedDefaultGeneratorFqn = GetJsonString(prop, "computedDefaultGeneratorFqn"),
                // The other end of the channel: without it the host's diff cannot tell a rename from
                // a drop and an add.
                RenamedFrom = GetJsonString(prop, "renamedFrom"),
                ValueObjectColumns = ParseValueObjectColumns(prop)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ValueObjectColumnModel> ParseValueObjectColumns(JsonElement prop)
    {
        if (!prop.TryGetProperty("valueObjectColumns", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ImmutableArray<ValueObjectColumnModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<ValueObjectColumnModel>();
        foreach (var col in arr.EnumerateArray())
        {
            var columnName = GetJsonString(col, "columnName");
            var typeName = GetJsonString(col, "typeName");
            if (columnName is null || typeName is null)
                continue;

            builder.Add(new ValueObjectColumnModel
            {
                ColumnName = columnName,
                TypeName = typeName,
                IsNullable = GetJsonBool(col, "isNullable"),
                IsEnum = GetJsonBool(col, "isEnum")
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<NavigationMetadataModel> ParseNavigationsFromJson(JsonElement parent)
    {
        if (!parent.TryGetProperty("navigations", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ImmutableArray<NavigationMetadataModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<NavigationMetadataModel>();
        foreach (var nav in arr.EnumerateArray())
        {
            var name = GetJsonString(nav, "name");
            var targetTypeName = GetJsonString(nav, "targetTypeName");
            var navigationType = GetJsonString(nav, "navigationType");
            if (name is null || targetTypeName is null || navigationType is null)
                continue;

            builder.Add(new NavigationMetadataModel
            {
                Name = name,
                TargetTypeName = targetTypeName,
                NavigationType = navigationType,
                InverseProperty = GetJsonString(nav, "inverseProperty"),
                ForeignKeyProperty = GetJsonString(nav, "foreignKeyProperty"),
                OnDelete = GetJsonString(nav, "onDelete") ?? "NoAction",
                IsRequired = GetJsonBool(nav, "isRequired"),
                TargetFullTypeName = GetJsonString(nav, "targetFullTypeName"),
                TargetBoundaryTypeFullName = GetJsonString(nav, "targetBoundaryTypeFullName"),
                IsFromRelationAttribute = GetJsonBool(nav, "isFromRelationAttribute"),
                IsPrincipal = GetJsonBool(nav, "isPrincipal"),
                IsOwned = GetJsonBool(nav, "isOwned"),
                JoinTable = GetJsonString(nav, "joinTable"),
                JoinEntityTypeName = GetJsonString(nav, "joinEntityTypeName"),
                JoinLeftKey = GetJsonString(nav, "joinLeftKey"),
                JoinRightKey = GetJsonString(nav, "joinRightKey")
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>A JSON array of strings, or empty when absent or not an array.</summary>
    private static ImmutableArray<string> ReadStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value)
                builder.Add(value);

        return builder.ToImmutable();
    }

    private static ImmutableArray<RelationAttributeModel> ParseRelationAttributesFromJson(JsonElement parent)
    {
        if (!parent.TryGetProperty("relationAttributes", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ImmutableArray<RelationAttributeModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<RelationAttributeModel>();
        foreach (var rel in arr.EnumerateArray())
        {
            var relationType = GetJsonString(rel, "relationType");
            var targetTypeFullName = GetJsonString(rel, "targetTypeFullName");
            var targetTypeName = GetJsonString(rel, "targetTypeName");
            if (relationType is null || targetTypeFullName is null || targetTypeName is null)
                continue;

            builder.Add(new RelationAttributeModel
            {
                RelationType = relationType,
                TargetTypeFullName = targetTypeFullName,
                TargetTypeName = targetTypeName,
                NavigationName = GetJsonString(rel, "navigationName"),
                InverseProperty = GetJsonString(rel, "inverseProperty"),
                ForeignKeyProperty = GetJsonString(rel, "foreignKeyProperty"),
                OnDelete = GetJsonString(rel, "onDelete") ?? "NoAction",
                IsRequired = !rel.TryGetProperty("isRequired", out var isReq) || GetJsonBool(rel, "isRequired", true),
                IsPrincipal = GetJsonBool(rel, "isPrincipal"),
                JoinTable = GetJsonString(rel, "joinTable"),
                JoinEntityTypeName = GetJsonString(rel, "joinEntityTypeName"),
                JoinLeftKey = GetJsonString(rel, "joinLeftKey"),
                JoinRightKey = GetJsonString(rel, "joinRightKey"),
                TargetBoundaryTypeFullName = GetJsonString(rel, "targetBoundaryTypeFullName"),
                IsExplicit = GetJsonBool(rel, "isExplicit")
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> ParseStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var value = item.GetString();
            if (value is not null)
                builder.Add(value);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Normalizes full ID types back to short form (e.g., "System.Guid" → "Guid").
    /// </summary>
    private static string NormalizeIdType(string idType)
    {
        return idType switch
        {
            "System.Guid" => "Guid",
            _ => idType
        };
    }

    // =========================================================================
    // JSON helpers (local to avoid dependency on Composition MetadataReader)
    // =========================================================================

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    /// <summary>
    ///     The entity's domain key, as written by <c>PersistenceMetadataTemplate</c>.
    /// </summary>
    /// <remarks>
    ///     Read as an array. It was a single <c>logicKey</c>/<c>logicKeyType</c> pair, and the pair had
    ///     no way to say that a room type is unique per property rather than globally.
    /// </remarks>
    private static EquatableArray<LogicKeyPart> ReadLogicKeys(JsonElement element)
    {
        if (!element.TryGetProperty("logicKeys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            return EquatableArray<LogicKeyPart>.Empty;

        var parts = ImmutableArray.CreateBuilder<LogicKeyPart>();
        foreach (var key in keys.EnumerateArray())
        {
            var name = GetJsonString(key, "name");
            if (string.IsNullOrEmpty(name))
                continue;

            parts.Add(new LogicKeyPart
            {
                Name = name!,
                TypeName = GetJsonString(key, "type") ?? "string",
                IsGlobal = GetJsonString(key, "scope") == "global"
            });
        }

        return parts.ToImmutable();
    }

    /// <summary>
    ///     The <c>[Unique]</c> indexes of an entity declared in a referenced assembly.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The entity configuration is generated in the host, from this metadata, so an index that
    ///     does not cross this channel does not exist as far as the database is concerned — the
    ///     attribute is in the module, the constraint is nowhere, and both sides compile.
    /// </remarks>
    private static EquatableArray<UniqueIndexModel> ReadUniqueIndexes(JsonElement element)
    {
        if (!element.TryGetProperty("uniqueIndexes", out var declared)
            || declared.ValueKind != JsonValueKind.Array)
        {
            return EquatableArray<UniqueIndexModel>.Empty;
        }

        var indexes = ImmutableArray.CreateBuilder<UniqueIndexModel>();
        foreach (var index in declared.EnumerateArray())
        {
            var columns = GetJsonString(index, "columns");
            if (string.IsNullOrEmpty(columns))
                continue;

            indexes.Add(new UniqueIndexModel
            {
                Columns = columns!.Split(',').ToImmutableArray(),
                IsGlobal = GetJsonString(index, "scope") == "global"
            });
        }

        return indexes.ToImmutable();
    }

    private static bool GetJsonBool(JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }

        return defaultValue;
    }

    private static int GetJsonInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    private static int? GetJsonIntNullable(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }

    /// <summary>
    ///     The <c>[VisibleWhen&lt;TRule&gt;]</c> rules the module published, fully qualified.
    /// </summary>
    /// <remarks>
    ///     The host installs each of these as an EF Core named global query filter on the entity, so
    ///     an entity whose rules do not survive this channel is generated exactly as an entity with no
    ///     rule — no diagnostic, no missing symbol, just rows that were meant to be invisible and are
    ///     not. Written by <c>PersistenceMetadataTemplate.RenderFilterMetadata</c>.
    /// </remarks>
    private static ImmutableArray<string> ReadVisibilityRules(JsonElement element)
    {
        if (!element.TryGetProperty("filters", out var filters)
            || filters.ValueKind != JsonValueKind.Object
            || !filters.TryGetProperty("visibilityRules", out var rules)
            || rules.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.String && rule.GetString() is { Length: > 0 } value)
                builder.Add(value);
        }

        return builder.ToImmutable();
    }
}
