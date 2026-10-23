using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The maps, readers and writers registered at module start for the entities and DTOs this assembly declares.</summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>
    ///     The entity readers the plans call (for every entity this assembly queries), and the maps the runtime path translates
    ///     with, registered at module start by the assembly that declares the entity or the DTO: one description per type.
    /// </summary>
    private static EntityMapsModel ReadMaps(List<EntityModel> entities, List<(INamedTypeSymbol Dto, EntityModel Entity, Projection Projection, List<string> Joins)> projections, List<AppFunction> functions, Compilation compilation, Func<INamedTypeSymbol, EntityModel?> lookup)
    {
        // The pools of the [Interned] columns this assembly reads: one per column, shared by the entity's reader, the projections and the runtime path.
        var pools = entities.SelectMany(e => e.Columns).Where(c => c.InternPool is not null).Select(c => (Field: c.InternPool!, Key: c.InternKey!)).Distinct().OrderBy(p => p.Field, StringComparer.Ordinal).Select(p => new InternPoolModel(p.Field, p.Key));
        var maps = entities.Where(e => !e.IsRawShape && SymbolEqualityComparer.Default.Equals(e.Type.ContainingAssembly, compilation.Assembly)).Select(e => MapOf(e, lookup));
        var readers = entities.Select(e => new EntityReaderModel(e.FullName, Safe(e), e.Construct("r", "d", static i => i.ToString())));
        return new EntityMapsModel(pools.ToEquatableArray(), maps.ToEquatableArray(), projections.Select(ProjectionMapOf).ToEquatableArray(), FunctionRegistrations(functions).ToEquatableArray(), readers.ToEquatableArray(), PrivateSetters(entities).ToEquatableArray());
    }

    private static EntityMapModel MapOf(EntityModel entity, Func<INamedTypeSymbol, EntityModel?> lookup)
    {
        var keys = entity.Keys;
        // [DatabaseGenerated] columns stay out of every write and come back by key after it.
        var written = entity.Columns.Where(c => c.Generated == 0).ToList();
        var filled = entity.Columns.Where(c => c.Generated > 0).ToList();
        // Table per type: the root's columns go to the root's table, the key and each level's own columns to that level's table (a part per
        // level below the root, nested: Car's then SportsCar's); the discriminator is the view's, never written.
        var levels = new List<(EntityModel Model, HashSet<string> Own)>();
        for (var level = entity.Type; entity.PartTable is not null && level is not null && lookup(level) is { PartTable: not null } levelModel; level = level.BaseType)
            levels.Insert(0, (levelModel, new HashSet<string>(level.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name))));
        var rootColumns = entity.Tpt ? written.Where(c => !c.IsDiscriminator && c.DerivedOf is null && !levels.Any(l => l.Own.Contains(c.Property))).ToList() : written;
        WriterModel? part = null;
        for (var i = levels.Count - 1; i >= 0; i--)
            part = WriterOf(entity, written.Where(c => keys.Contains(c) || (c.DerivedOf is null && levels[i].Own.Contains(c.Property))).ToList(), levels[i].Model.PartTable!, [], part, root: false);
        var generated = filled.Select((c, i) => new GeneratedColumnModel(c.Column, c.Property, c.Read("r", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "d"))).ToList();
        var writer = WriterOf(entity, rootColumns, entity.WriteTable ?? entity.Table, generated, part, root: true);
        // [ConcurrencyAware]: where the database keeps the version (xmin, rowversion) an insert binds every column but it.
        var native = rootColumns.FindIndex(c => c.IsRowVersion);
        writer = writer with
        {
            GeneratedOnUpdates = filled.Any(c => c.Generated == 2),
            Stamps = entity.Stamps(), // [Auditable], [SoftDelete]: the root's table holds the stamped columns
            HasNativeVersion = native >= 0,
            NativeVersionBinds = native < 0 ? EquatableArray<string>.Empty : rootColumns.Where((_, i) => i != native).Select(c => c.Bind(c.Value("e"))).ToEquatableArray(),
        };

        // A view has no writer: the session says so when asked to write it. A TPC root's writes go to the row's concrete type.
        var mode = entity.ReadOnly ? EntityWriteMode.ReadOnly : entity.Tpc && entity.IsHierarchyRoot ? EntityWriteMode.Dispatch : entity.Tpt ? EntityWriteMode.DispatchWithWriter : EntityWriteMode.Writer;
        var rollUps = new List<RollUpModel>();
        foreach (var rollUp in entity.Columns.Where(c => c.RollUpChild is not null))
        {
            // The rule goes with the parent's map; the child's writer finds it by its table on its first write.
            if (lookup(rollUp.RollUpChild!) is not { } child || keys.Count != 1)
                continue;
            var foreignKey = child.Column(entity.Type.Name + "Id")?.Column ?? entity.Type.Name + "Id";
            var amount = rollUp.RollUpProperty is null ? null : child.Column(rollUp.RollUpProperty)?.Column ?? rollUp.RollUpProperty;
            rollUps.Add(new RollUpModel(entity.WriteTable ?? entity.Table, keys[0].Column, rollUp.Column, child.WriteTable ?? child.Table, foreignKey, amount, child.SoftDeleteColumn)); // a soft-deleted child counts for nothing
        }

        return new EntityMapModel(
            entity.FullName,
            entity.Table,
            keys.Select(k => k.Column).ToEquatableArray(),
            entity.Columns.Select(ColumnMapOf).ToEquatableArray(),
            entity.Navigations.Select(n => NavigationMapOf(entity, n, lookup)).ToEquatableArray(), // a composite navigation with one foreign key column per key column of its target
            entity.Collections.Select(c => CollectionMapOf(entity, c, lookup)).ToEquatableArray(),
            entity.Rules.Select(r => r.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToEquatableArray(),
            Safe(entity),
            (keys.Count > 0 ? keys[0] : entity.Columns[0]).Property,
            entity.FixedPredicate,
            mode,
            mode is EntityWriteMode.Writer or EntityWriteMode.DispatchWithWriter ? writer : null,
            entity.SoftDeleteColumn, // the runtime path's reads leave the deleted rows out
            entity.SoftDeleteCascade,
            entity.IsPragmatic, // its rows depend on the caller: the runtime path refuses it until it applies the families
            keys.Count > 1 ? keys.Select(k => k.Property).ToEquatableArray() : EquatableArray<string>.Empty, // what a composite parent's children group on
            rollUps.ToEquatableArray());
    }

    /// <summary>A JSON column carries its mark, and the members whose JSON name is not their own, for the runtime path's JSON paths.</summary>
    private static ColumnMapModel ColumnMapOf(ColumnModel c)
    {
        var json = LambdaTranslator.IsJsonConverter(c.Converter);
        return new ColumnMapModel(
            c.Property,
            c.Column,
            LambdaTranslator.KindOf(c.Store),
            c.Read("r", "i", "d"),
            c.Converter,
            c.Converter is null ? null : EntityModel.Unwrap(c.Type).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            json,
            json ? JsonNames.Renamed(c.Type).Select(r => new JsonRenameModel(r.Path, r.Name)).ToEquatableArray() : EquatableArray<JsonRenameModel>.Empty,
            c.FullTextTable,
            c.FullTextKey);
    }

    /// <summary>
    ///     The writer of <paramref name="written" /> into <paramref name="table" />. A table-per-type part (<paramref name="root" /> false) has no
    ///     version and no identity: the root's table holds them.
    /// </summary>
    private static WriterModel WriterOf(EntityModel entity, List<ColumnModel> written, string table, List<GeneratedColumnModel> generated, WriterModel? part, bool root)
    {
        var keys = entity.Keys;
        var keyIndex = keys.Count > 0 ? written.IndexOf(keys[0]) : 0;
        var versionIndex = !root || entity.Version is null ? -1 : written.FindIndex(c => c.Property == entity.Version);
        // The upsert reads the version back; an identity entity binds its inserts without the key and reads the key back.
        IdentityModel? identity = null;
        if (root && entity.Identity)
            identity = new IdentityModel(written.Where((_, i) => i != keyIndex).Select(c => c.Bind(c.Value("e"))).ToEquatableArray(), written[keyIndex].Property, written[keyIndex].Read("r", "0", "d"));
        return new WriterModel(
            entity.FullName,
            table,
            written.Select(c => new WrittenColumnModel(c.Column, c.Property, LambdaTranslator.KindOf(c.Store), c.Bind(c.Value("e")), c.Cast)).ToEquatableArray(), // the casts: PostgreSQL's on writes ([Column(Cast = "jsonb")])
            keys.Select(k => written.IndexOf(k)).ToEquatableArray(),
            versionIndex,
            versionIndex >= 0 ? entity.Version : null,
            versionIndex >= 0 ? written[versionIndex].Read("r", "0", "d") : null,
            keys.Select(k => k.Bind(k.Value("e"))).ToEquatableArray(),
            keys.Select(k => k.Property).ToEquatableArray(),
            identity,
            generated.ToEquatableArray(),
            GeneratedOnUpdates: false,
            part,
            Stamps: null,
            EquatableArray<string>.Empty,
            HasNativeVersion: false);
    }

    /// <summary>A to-one navigation: the join column, and the target read at an offset (its columns in the target's order) when the target entity is known.</summary>
    private static NavigationMapModel NavigationMapOf(EntityModel entity, NavigationModel navigation, Func<INamedTypeSymbol, EntityModel?> lookup)
        => new(
            entity.FullName,
            navigation.Property,
            entity.Column(navigation.ForeignKey)?.Column ?? navigation.ForeignKey,
            navigation.Required,
            navigation.Target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            lookup(navigation.Target)?.Construct("r", "d", static i => "o + " + i),
            navigation.IsComposite ? navigation.ForeignKeys.Select(fk => entity.Column(fk)?.Column ?? fk).ToEquatableArray() : EquatableArray<string>.Empty); // one per key column of the target

    /// <summary>
    ///     A collection: a many-to-many side carries the join table and its child column, its child key being the child's own key (the links
    ///     are matched on it); a composite parent's children are grouped on all their foreign keys (a link's child: on its whole key).
    /// </summary>
    private static CollectionMapModel CollectionMapOf(EntityModel entity, CollectionModel c, Func<INamedTypeSymbol, EntityModel?> lookup)
    {
        var childModel = lookup(c.Child);
        var compositeLinkChild = c.IsLink && childModel is { HasCompositeKey: true };
        var compositeParent = c.IsComposite && !c.IsLink;
        IEnumerable<string> childKey = compositeLinkChild ? childModel!.Keys.Select(k => k.Property) : compositeParent ? c.ForeignKeys : new[] { c.IsLink ? childModel?.KeyProperty ?? "Id" : c.ForeignKey };
        return new CollectionMapModel(
            entity.FullName,
            c.Property,
            c.Child.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            childModel is null ? c.ForeignKey : c.ForeignKeyColumn(childModel),
            childKey.ToEquatableArray(),
            compositeLinkChild || compositeParent,
            c.LinkTable,
            c.LinkChildColumn,
            c.IsLink && c.LinkChildColumns.Length > 1 ? c.LinkChildColumns.ToEquatableArray() : EquatableArray<string>.Empty,
            c.IsComposite && childModel is not null ? c.ForeignKeyColumns(childModel).ToEquatableArray() : EquatableArray<string>.Empty);
    }

    /// <summary>The DTO's collection members: the child DTO's own registration gives its columns and reader; here the key the loader groups on.</summary>
    private static ProjectionMapModel ProjectionMapOf((INamedTypeSymbol Dto, EntityModel Entity, Projection Projection, List<string> Joins) declared)
    {
        var (dto, entity, projection, joins) = declared;
        return new ProjectionMapModel(
            entity.FullName,
            dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            projection.Columns.ToEquatableArray(),
            joins.ToEquatableArray(),
            projection.Construct,
            projection.Collections.Select(c => new ProjectedCollectionModel(c.Property, c.Child.FullName, c.ChildDtoName, c.ForeignKeyColumn, c.ForeignKeyColumns.ToEquatableArray())).ToEquatableArray(),
            projection.KeyProperties is not null, // a composite parent: its key as the DTO's properties hold it, the child's foreign key columns
            (projection.KeyProperties ?? []).ToEquatableArray(),
            projection.KeyProperty);
    }

    /// <summary>
    ///     The private setters the rows are built through (Pragmatic's generated <c>OwnerId</c>, <c>{Navigation}Id</c>): an <c>[UnsafeAccessor]</c>
    ///     on the declaring type, no reflection (Native AOT), and a helper that returns the row, generic so a derived row keeps its type.
    /// </summary>
    private static IEnumerable<PrivateSetterModel> PrivateSetters(List<EntityModel> entities)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in entities.SelectMany(e => e.Columns).Where(c => c.PrivateSetter && c.Owner is null))
        {
            if (emitted.Add(column.SetterName))
                yield return new PrivateSetterModel(column.SetterName, column.SetterOwner!, column.Type.ToDisplayString(Annotated), column.Property);
        }
    }
}
