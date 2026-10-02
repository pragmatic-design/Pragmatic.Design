using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.Persistence, ...)] attribute
///     with per-entity repository info for direct DI registration by the host.
/// </summary>
internal sealed class PersistenceMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EntityMetadataModel> _entities;
    private readonly bool _indent;

    public PersistenceMetadataTemplate(
        ImmutableArray<EntityMetadataModel> entities,
        bool indent)
    {
        _entities = entities;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Persistence"),
        ToSourceText());

    protected override bool Validate() => !_entities.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Persistence, \"{MetadataSchemaVersions.Persistence}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    /// <summary>
    ///     The metadata document this template writes. Public because a host that declares its own
    ///     entities has to hand the same document to Composition directly — the attribute above is
    ///     emitted into that same compilation and can never be read back off it.
    /// </summary>
    public string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Persistence.EFCore.SourceGenerator");

        // The query-filter registration, so the host calls it. The repositories stay inline — the
        // host builds those from the payload below — but the filters live in a generated method that,
        // left to the application, would leave [HasAccessScopes]/[HasOwner] unenforced in any
        // application that did not know to call it.
        var queryFilters = QueryFilterRegistrationTemplate.RegistrationMethodFor(_entities);
        if (queryFilters is null)
            builder.PropertyNull("registrationMethod");
        else
            builder.Property("registrationMethod", queryFilters);

        // The lookup-cache registration, for the same reason and by the same route. It has a field of
        // its own because "registrationMethod" is one slot and the query filters hold it. Without this
        // the method is generated per assembly and called by nothing inside a host: no
        // ILookupCache<T, TId> in the container, no loader, no preload — and the documentation had
        // turned that into an instruction to the reader instead of a defect.
        var lookupRegistration = LookupCacheRegistrationTemplate.RegistrationMethodFor(
            _entities.Where(e => e is { IsLookup: true, IsFromReference: false }).ToImmutableArray());
        if (lookupRegistration is null)
            builder.PropertyNull("lookupRegistrationMethod");
        else
            builder.Property("lookupRegistrationMethod", lookupRegistration);

        builder.Property("data");
        builder.StartObject();
        builder.Property("entitiesCount", _entities.Length);

        builder.Property("entities");
        builder.StartArray();

        foreach (var entity in _entities.OrderBy(e => e.FullTypeName))
        {
            builder.StartObject();
            builder.Property("type", entity.FullTypeName);
            builder.Property("idType", GetFullIdType(entity.IdType));
            builder.Property("repositoryType", $"{entity.Namespace}.{entity.TypeName}.Repository");

            // Enriched entity metadata (schema 1.1+) — allows host to reconstruct
            // EntityMetadataModel from JSON without full type scan
            builder.Property("namespace", entity.Namespace);
            builder.Property("typeName", entity.TypeName);
            builder.Property("accessibility", entity.Accessibility);
            builder.Property("isAbstract", entity.IsAbstract);
            builder.Property("isAuditable", entity.IsAuditable);
            builder.Property("isAudited", entity.IsAudited);
            builder.Property("isConcurrencyAware", entity.IsConcurrencyAware);
            builder.Property("isSoftDelete", entity.IsSoftDelete);
            builder.Property("isSoftDeleteCascade", entity.IsSoftDeleteCascade);
            builder.Property("isTenantEntity", entity.IsTenantEntity);
            builder.Property("isOwnedEntity", entity.IsOwnedEntity);
            builder.Property("isScopedEntity", entity.IsScopedEntity);
            builder.Property("usesRelationAttributes", entity.UsesRelationAttributes);

            // Written only when true: the host needs it to map the subject registry beside the subject,
            // And every entity that is not one reads as false when it is absent.
            if (entity.IsDataSubject)
                builder.Property("isDataSubject", true);

            if (!string.IsNullOrEmpty(entity.BoundaryName))
                builder.Property("boundaryName", entity.BoundaryName);
            if (!string.IsNullOrEmpty(entity.BoundaryTypeFullName))
                builder.Property("boundaryTypeFullName", entity.BoundaryTypeFullName);
            // The whole domain key, in declaration order — an array, because it is very often more
            // than one column and the single logicKey/logicKeyType pair could not say so.
            if (entity.LogicKeys.Length > 0)
            {
                builder.Property("logicKeys").StartArray();
                foreach (var part in entity.LogicKeys)
                {
                    builder.StartObject();
                    builder.Property("name", part.Name);
                    builder.Property("type", part.TypeName);

                    // ⚠️ The scope has to cross too. The entity configuration is generated in the host,
                    // from this metadata, so a key that asked to be global in its own module and did
                    // not say so here would come out per-tenant on the other side — with a schema that
                    // disagrees with the attribute and nothing to read that says so.
                    if (part.IsGlobal)
                        builder.Property("scope", "global");

                    builder.EndObject();
                }

                builder.EndArray();
            }

            // [Unique] indexes, for the same reason: the host writes the configuration and can only
            // write what reaches it.
            if (entity.UniqueIndexes.Length > 0)
            {
                builder.Property("uniqueIndexes").StartArray();
                foreach (var index in entity.UniqueIndexes)
                {
                    builder.StartObject();
                    builder.Property("columns", string.Join(",", index.Columns));
                    if (index.IsGlobal)
                        builder.Property("scope", "global");
                    builder.EndObject();
                }

                builder.EndArray();
            }

            // Manual trait flags
            if (entity.HasManualPersistenceId)
                builder.Property("hasManualPersistenceId", true);
            if (entity.HasManualAuditableProps)
                builder.Property("hasManualAuditableProps", true);
            if (entity.HasManualSoftDeleteProps)
                builder.Property("hasManualSoftDeleteProps", true);
            if (entity.HasManualEntityInterface)
                builder.Property("hasManualEntityInterface", true);
            if (entity.HasManualAuditableInterface)
                builder.Property("hasManualAuditableInterface", true);
            if (entity.HasManualSoftDeleteInterface)
                builder.Property("hasManualSoftDeleteInterface", true);

            // Filter metadata
            RenderFilterMetadata(builder, entity);

            // Lifecycle metadata
            RenderLifecycleMetadata(builder, entity);

            // Temporal metadata
            if (entity.IsTemporalRelation)
                RenderTemporalMetadata(builder, entity);

            // Lookup metadata
            if (entity.IsLookup)
                builder.Property("isLookup", true);

            // Custom table/schema
            if (!string.IsNullOrEmpty(entity.CustomTableName))
                builder.Property("customTableName", entity.CustomTableName);
            if (!string.IsNullOrEmpty(entity.CustomSchemaName))
                builder.Property("customSchemaName", entity.CustomSchemaName);

            // Inheritance metadata
            if (!string.IsNullOrEmpty(entity.InheritanceStrategy))
                builder.Property("inheritanceStrategy", entity.InheritanceStrategy);
            if (!string.IsNullOrEmpty(entity.DiscriminatorColumn))
                builder.Property("discriminatorColumn", entity.DiscriminatorColumn);
            if (!string.IsNullOrEmpty(entity.BaseEntityFullTypeName))
                builder.Property("baseEntityFullTypeName", entity.BaseEntityFullTypeName);

            // Timeline metadata
            if (entity.HasGenerateTimeline)
                builder.Property("hasGenerateTimeline", true);

            // Properties array
            RenderPropertiesMetadata(builder, entity);

            // Navigations array
            RenderNavigationsMetadata(builder, entity);

            // Relation attributes array (needed for cross-entity graph building at host level)
            RenderRelationAttributesMetadata(builder, entity);

            // Source member names (for deduplication in RelationGraphBuilder)
            if (!entity.AllSourceMemberNames.IsDefaultOrEmpty)
                builder.PropertyArray("allSourceMemberNames", entity.AllSourceMemberNames);

            // Ignored property names
            if (!entity.IgnoredPropertyNames.IsDefaultOrEmpty)
                builder.PropertyArray("ignoredPropertyNames", entity.IgnoredPropertyNames);

            builder.EndObject();
        }

        builder.EndArray();
        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }

    private static string GetFullIdType(string idType)
    {
        return idType switch
        {
            "Guid" => "System.Guid",
            "int" => "int",
            "long" => "long",
            "string" => "string",
            _ => idType
        };
    }

    /// <summary>
    ///     The filters the host has to install for this entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The host reads entities from here in preference to scanning the referenced assembly for
    ///         symbols, and only falls back to the scan for an assembly that published nothing. So a
    ///         fact that is not written here does not exist as far as the host is concerned, however
    ///         plainly it is declared in the module: the declared visibility rules were read by all
    ///         three symbol transforms and by neither reader, which is why the entity configuration
    ///         generated for them was silently identical to one with no rule at all.
    ///     </para>
    /// </remarks>
    private static void RenderFilterMetadata(MetadataJsonBuilder builder, EntityMetadataModel entity)
    {
        if (entity is { IsSoftDelete: false, IsTenantEntity: false, IsTemporalRelation: false, IsOwnedEntity: false, IsScopedEntity: false }
            && entity.VisibilityRules.Length == 0)
        {
            return;
        }

        builder.Property("filters");
        builder.StartObject();
        if (entity.IsSoftDelete)
            builder.Property("softDelete", true);
        if (entity.IsTenantEntity)
            builder.Property("tenant", true);
        if (entity.IsTemporalRelation)
            builder.Property("temporal", true);
        if (entity.IsOwnedEntity)
            builder.Property("ownership", true);
        if (entity.IsScopedEntity)
            builder.Property("scoped", true);
        if (entity.VisibilityRules.Length > 0)
            builder.PropertyArray("visibilityRules", entity.VisibilityRules.AsImmutableArray());
        builder.EndObject();
    }

    private static void RenderLifecycleMetadata(MetadataJsonBuilder builder, EntityMetadataModel entity)
    {
        var hasStaticDefaults = entity.Properties.Any(p => p.DefaultValueExpression is not null);
        var hasComputedDefaults = entity.Properties.Any(p => p.ComputedDefaultGeneratorFqn is not null);
        if (!hasStaticDefaults && !hasComputedDefaults)
            return;

        builder.Property("lifecycle");
        builder.StartObject();

        if (hasStaticDefaults)
        {
            builder.Property("staticDefaults");
            builder.StartArray();
            foreach (var prop in entity.Properties.Where(p => p.DefaultValueExpression is not null))
            {
                builder.StartObject();
                builder.Property("property", prop.Name);
                builder.Property("value", prop.DefaultValueExpression!);
                builder.EndObject();
            }

            builder.EndArray();
        }

        if (hasComputedDefaults)
        {
            builder.Property("computedDefaults");
            builder.StartArray();
            foreach (var prop in entity.Properties.Where(p => p.ComputedDefaultGeneratorFqn is not null))
            {
                builder.StartObject();
                builder.Property("property", prop.Name);
                builder.Property("generator", prop.ComputedDefaultGeneratorFqn!);
                builder.EndObject();
            }

            builder.EndArray();
        }

        builder.EndObject();
    }

    private static void RenderTemporalMetadata(MetadataJsonBuilder builder, EntityMetadataModel entity)
    {
        builder.Property("temporal");
        builder.StartObject();
        builder.Property("maxActive", entity.TemporalMaxActive);
        builder.Property("allowOverlap", entity.TemporalAllowOverlap);
        if (entity.TemporalParentTypeName is not null)
            builder.Property("parentTypeName", entity.TemporalParentTypeName);
        if (entity.TemporalParentTypeFullName is not null)
            builder.Property("parentTypeFullName", entity.TemporalParentTypeFullName);
        if (entity.TemporalChildTypeName is not null)
            builder.Property("childTypeName", entity.TemporalChildTypeName);
        if (entity.TemporalChildTypeFullName is not null)
            builder.Property("childTypeFullName", entity.TemporalChildTypeFullName);
        if (entity.TemporalParentFkProperty is not null)
            builder.Property("parentFkProperty", entity.TemporalParentFkProperty);
        if (entity.TemporalChildFkProperty is not null)
            builder.Property("childFkProperty", entity.TemporalChildFkProperty);
        builder.EndObject();
    }

    private static void RenderPropertiesMetadata(MetadataJsonBuilder builder, EntityMetadataModel entity)
    {
        if (entity.Properties.IsDefaultOrEmpty)
            return;

        builder.Property("properties");
        builder.StartArray();
        foreach (var prop in entity.Properties)
        {
            builder.StartObject();
            builder.Property("name", prop.Name);
            builder.Property("typeName", prop.TypeName);
            if (prop.HasPrivateSetter)
                builder.Property("hasPrivateSetter", true);
            if (prop.IsNullable)
                builder.Property("isNullable", true);
            if (prop.IsEnum)
                builder.Property("isEnum", true);
            if (prop.IsNavigation)
                builder.Property("isNavigation", true);
            if (prop.IsLogicKey)
                builder.Property("isLogicKey", true);
            if (prop.HasDefaultValue)
                builder.Property("hasDefaultValue", true);
            if (prop.MaxLength is > 0)
                builder.Property("maxLength", prop.MaxLength.Value);
            if (prop.Precision is > 0)
                builder.Property("precision", prop.Precision.Value);
            if (prop.Scale is > 0)
                builder.Property("scale", prop.Scale.Value);
            if (prop.DefaultValueExpression is not null)
                builder.Property("defaultValueExpression", prop.DefaultValueExpression);
            if (prop.ComputedDefaultGeneratorFqn is not null)
                builder.Property("computedDefaultGeneratorFqn", prop.ComputedDefaultGeneratorFqn);
            // ⚠️ The migration is written by the HOST, and this payload is the only thing it reads an
            // entity of another assembly through. Dropped here, [RenamedFrom] was read by the module's
            // generator, never published, and the diff saw a column added and a column removed — so a
            // rename became a drop with the data in it. Every Pragmatic application puts its entities
            // in modules, which made the attribute work only where nobody keeps one.
            if (prop.RenamedFrom is not null)
                builder.Property("renamedFrom", prop.RenamedFrom);
            if (!prop.ValueObjectColumns.IsDefaultOrEmpty)
            {
                builder.Property("valueObjectColumns");
                builder.StartArray();
                foreach (var col in prop.ValueObjectColumns)
                {
                    builder.StartObject();
                    builder.Property("columnName", col.ColumnName);
                    builder.Property("typeName", col.TypeName);
                    if (col.IsNullable)
                        builder.Property("isNullable", true);
                    if (col.IsEnum)
                        builder.Property("isEnum", true);
                    builder.EndObject();
                }
                builder.EndArray();
            }
            builder.EndObject();
        }

        builder.EndArray();
    }

    private static void RenderNavigationsMetadata(MetadataJsonBuilder builder, EntityMetadataModel entity)
    {
        if (entity.Navigations.IsDefaultOrEmpty)
            return;

        builder.Property("navigations");
        builder.StartArray();
        foreach (var nav in entity.Navigations)
        {
            builder.StartObject();
            builder.Property("name", nav.Name);
            builder.Property("targetTypeName", nav.TargetTypeName);
            builder.Property("navigationType", nav.NavigationType);
            if (nav.InverseProperty is not null)
                builder.Property("inverseProperty", nav.InverseProperty);
            if (nav.ForeignKeyProperty is not null)
                builder.Property("foreignKeyProperty", nav.ForeignKeyProperty);
            if (nav.OnDelete != "NoAction")
                builder.Property("onDelete", nav.OnDelete);
            if (nav.IsRequired)
                builder.Property("isRequired", true);
            if (nav.TargetFullTypeName is not null)
                builder.Property("targetFullTypeName", nav.TargetFullTypeName);
            if (nav.TargetBoundaryTypeFullName is not null)
                builder.Property("targetBoundaryTypeFullName", nav.TargetBoundaryTypeFullName);
            if (nav.IsFromRelationAttribute)
                builder.Property("isFromRelationAttribute", true);
            if (nav.IsPrincipal)
                builder.Property("isPrincipal", true);
            if (nav.IsOwned)
                builder.Property("isOwned", true);
            if (nav.JoinTable is not null)
                builder.Property("joinTable", nav.JoinTable);
            if (nav.JoinEntityTypeName is not null)
                builder.Property("joinEntityTypeName", nav.JoinEntityTypeName);
            // The host builds the entity configuration from this metadata, not from the model in
            // memory: keys that stop here leave it emitting a bare UsingEntity<T>(), which is the
            // shadow-foreign-key failure the names exist to prevent.
            if (nav.JoinLeftKey is { Length: > 0 })
                builder.Property("joinLeftKey", nav.JoinLeftKey);
            if (nav.JoinRightKey is { Length: > 0 })
                builder.Property("joinRightKey", nav.JoinRightKey);
            builder.EndObject();
        }

        builder.EndArray();
    }

    private static void RenderRelationAttributesMetadata(MetadataJsonBuilder builder, EntityMetadataModel entity)
    {
        if (entity.RelationAttributes.IsDefaultOrEmpty)
            return;

        // The entities this boundary reads: the host rebuilds the relation graph from these raw
        // declarations, and needs to know which cross-boundary targets are reachable anyway.
        builder.Property("readAccessTypes");
        builder.StartArray();
        foreach (var readable in entity.ReadAccessTypes)
            builder.Value(readable);
        builder.EndArray();

        builder.Property("relationAttributes");
        builder.StartArray();
        foreach (var rel in entity.RelationAttributes)
        {
            builder.StartObject();
            builder.Property("relationType", rel.RelationType);
            builder.Property("targetTypeFullName", rel.TargetTypeFullName);
            builder.Property("targetTypeName", rel.TargetTypeName);
            if (rel.NavigationName is not null)
                builder.Property("navigationName", rel.NavigationName);
            if (rel.InverseProperty is not null)
                builder.Property("inverseProperty", rel.InverseProperty);
            if (rel.ForeignKeyProperty is not null)
                builder.Property("foreignKeyProperty", rel.ForeignKeyProperty);
            if (rel.OnDelete != "NoAction")
                builder.Property("onDelete", rel.OnDelete);
            if (!rel.IsRequired)
                builder.Property("isRequired", false);
            if (rel.IsPrincipal)
                builder.Property("isPrincipal", true);
            if (rel.JoinTable is not null)
                builder.Property("joinTable", rel.JoinTable);
            if (rel.JoinEntityTypeName is not null)
                builder.Property("joinEntityTypeName", rel.JoinEntityTypeName);
            if (rel.JoinLeftKey is { Length: > 0 })
                builder.Property("joinLeftKey", rel.JoinLeftKey);
            if (rel.JoinRightKey is { Length: > 0 })
                builder.Property("joinRightKey", rel.JoinRightKey);
            if (rel.TargetBoundaryTypeFullName is not null)
                builder.Property("targetBoundaryTypeFullName", rel.TargetBoundaryTypeFullName);
            if (rel.IsExplicit)
                builder.Property("isExplicit", true);
            builder.EndObject();
        }

        builder.EndArray();
    }

}
