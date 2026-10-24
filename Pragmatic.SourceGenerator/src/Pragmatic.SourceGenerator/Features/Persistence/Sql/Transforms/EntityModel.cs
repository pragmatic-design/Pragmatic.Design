using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>An entity as the generator sees it: table, mapped columns with their reader, navigations with their foreign key, collections, visibility rules.</summary>
internal sealed partial class EntityModel
{
    public EntityModel(INamedTypeSymbol type, string table, List<ColumnModel> columns, List<NavigationModel> navigations, List<CollectionModel> collections, List<INamedTypeSymbol> rules, string? version, bool identity, List<OwnedModel>? owned = null, List<DerivedModel>? derived = null, string? fixedPredicate = null)
    {
        Type = type;
        Table = table;
        Columns = columns;
        Navigations = navigations;
        Collections = collections;
        Rules = rules;
        Version = version;
        Identity = identity;
        Owned = owned ?? [];
        Derived = derived;
        FixedPredicate = fixedPredicate;
    }

    /// <summary>The owned types flattened into this table (<c>[Owned]</c>): their columns are in <see cref="Columns" /> too, under <c>Owner.Member</c> names.</summary>
    public List<OwnedModel> Owned { get; }

    /// <summary>At a hierarchy's root: the derived types sharing its table, whose own columns follow the root's in <see cref="Columns" />; null otherwise.</summary>
    public List<DerivedModel>? Derived { get; }

    /// <summary>A predicate every plan on this entity carries, which the chain cannot drop: a derived type's rows by discriminator; null otherwise.</summary>
    public string? FixedPredicate { get; }

    /// <summary>True for a view (<c>[Table(ReadOnly = true)]</c>): read like a table, never written (no writer; a command through a chain is refused).</summary>
    public bool ReadOnly { get; set; } // set, not init: the generator compiles for netstandard2.0

    /// <summary>True in a table-per-concrete-type hierarchy: the root's table is the view over the derived types' tables; a write through the root goes to the row's type.</summary>
    public bool Tpc { get; set; }

    /// <summary>True for a lookup table (<c>[Table(Small = true)]</c>): a read of every row is its normal use, not reported (PRAG3021).</summary>
    public bool Small { get; set; }

    /// <summary>A derived type's rows among the root's, by discriminator (<c>t."Kind" = 'Derived'</c>): what <c>x is Derived</c> tests on the root's rows. The fixed predicate in TPH; on the root's view only in TPC.</summary>
    public string? DiscriminatorPredicate { get; set; }

    /// <summary>
    ///     The members of an object initializer reading the columns of <paramref name="subset" /> (every column when null): <c>Prop = …</c> for a
    ///     plain column, <c>Owner = new T { … }</c> for an owned type (null when optional and its first column is null); the discriminator and
    ///     the other derived types' columns are skipped. <paramref name="ordinal" /> gives the ordinal text of the i-th column of <see cref="Columns" />.
    /// </summary>
    public string Members(string reader, string dialect, Func<int, string> ordinal, Func<ColumnModel, bool>? subset = null)
    {
        var members = new List<string>();
        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];
            if (column.IsDiscriminator || (subset is not null && !subset(column)))
                continue;
            if (column.Owner is null)
            {
                members.Add($"{column.Property} = {column.Read(reader, ordinal(i), dialect)}");
                continue;
            }

            var owned = Owned.First(o => o.Property == column.Owner);
            if (owned.Columns[0] != column)
                continue; // the owned type's members are built together, at its first column
            var at = i;
            var construct = ConstructOwned(owned, m => owned.Columns[m].Read(reader, ordinal(at + m), dialect)); // an initializer, or the constructor of a value object
            members.Add(owned.Optional ? $"{owned.Property} = ({reader}.IsDBNull({ordinal(i)}) ? null : {construct})" : $"{owned.Property} = {construct}");
        }

        return string.Join(", ", members);
    }

    /// <summary>The <c>[Version]</c> property, the optimistic-concurrency column; null when the entity has none.</summary>
    public string? Version { get; }

    /// <summary>True when the key carries <c>[Identity]</c>: the database assigns it, the insert reads it back.</summary>
    public bool Identity { get; }

    public INamedTypeSymbol Type { get; }

    public string Table { get; }

    /// <summary>True for a DTO read as the rows of a hand-written statement: columns only, no table, no map or writer registered, no index analysis.</summary>
    public bool IsRawShape => Table.Length == 0;

    public List<ColumnModel> Columns { get; }

    public List<NavigationModel> Navigations { get; }

    /// <summary>The one-to-many sides: loaded by <c>Include</c> with a second query on the parent keys.</summary>
    public List<CollectionModel> Collections { get; }

    /// <summary>The <c>[VisibleWhen&lt;TRule&gt;]</c> rules: predicates every plan on the entity carries unless the chain says <c>WithoutFilters()</c>.</summary>
    public List<INamedTypeSymbol> Rules { get; }

    public string FullName => Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public NavigationModel? Navigation(string property) => Navigations.FirstOrDefault(n => n.Property == property);

    public CollectionModel? Collection(string property) => Collections.FirstOrDefault(c => c.Property == property);

    /// <summary>
    ///     The JOIN a navigation needs, in the fragment form (double-quoted identifiers, alias <c>j_Nav</c>): INNER when the foreign
    ///     key is required (a non-nullable property: the row always has its target, as EF Core assumes too), LEFT when it is optional.
    ///     A plan may still turn an optional navigation's join INNER when its predicate needs the joined row (see <c>PlanBuilder</c>).
    /// </summary>
    public string Join(NavigationModel navigation, EntityModel target) => Join(navigation, target, "t", navigation.Alias);

    /// <summary>
    ///     The same join from an included parent (<c>j_Order</c>) to its own navigation, under a nested alias (<c>j_Order_Customer</c>);
    ///     LEFT whatever the key says with <paramref name="optional" /> (a derived type's navigation reached through a cast: the row may be of another type).
    /// </summary>
    public string Join(NavigationModel navigation, EntityModel target, string parentAlias, string alias, bool optional = false)
        => $"{(navigation.Required && !optional ? "INNER" : "LEFT")} JOIN \"{target.Table}\" {alias} ON "
           + string.Join(" AND ", navigation.ForeignKeys.Select((fk, i) => $"{alias}.\"{(navigation.IsComposite ? target.Keys[i].Column : target.KeyColumn)}\" = {parentAlias}.\"{Column(fk)?.Column ?? fk}\""));

    /// <summary>True for a type that admits null: <c>Nullable&lt;T&gt;</c>, or a reference type annotated <c>?</c>.</summary>
    public static bool IsNullable(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } || (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated);

    public static bool IsEntity(ITypeSymbol? type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Class } named && named.GetAttributes().Any(a => a.AttributeClass?.Name == "EntityAttribute");

    /// <summary>A <c>[MapFrom]</c> DTO: the rows a hand-written statement may come back as.</summary>
    public static bool IsDeclaredDto(ITypeSymbol? type)
        => type is INamedTypeSymbol named && named.GetAttributes().Any(a => a.AttributeClass is { Name: "MapFromAttribute", TypeArguments.Length: 1 });

    /// <summary>
    ///     Reads the entity; null with a diagnostic when a property cannot be mapped. <paramref name="entities" /> are every [Entity] of the
    ///     compilation (the derived types of a hierarchy root are found among them); <paramref name="compilation" /> types the discriminator;
    ///     <paramref name="schema" /> is the committed snapshot's index list, the truth about the indexes of the tables it knows.
    /// </summary>
    public static EntityModel? Read(INamedTypeSymbol type, List<Diagnostic> diagnostics, IReadOnlyList<INamedTypeSymbol>? entities = null, Compilation? compilation = null, SchemaIndexes? schema = null)
    {
        // A hierarchy: the root carries [Inheritance], the derived types share its table (the root's name, or its [Table]).
        var root = HierarchyRootOf(type);
        var tpc = root is not null && IsTpc(root);
        var tableOwner = root is null || tpc ? type : root; // TPC: each type its own table (the root's is the view over them)
        var tableAttribute = tableOwner.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "TableAttribute");
        var table = tableAttribute is { ConstructorArguments.Length: > 0 } && tableAttribute.ConstructorArguments[0].Value is string named ? named : Pluralize(tableOwner.Name);
        var readOnly = tableAttribute?.NamedArguments.FirstOrDefault(n => n.Key == "ReadOnly").Value.Value is true;
        var small = tableAttribute?.NamedArguments.FirstOrDefault(n => n.Key == "Small").Value.Value is true
            || type.GetAttributes().Any(a => a.AttributeClass?.Name == "LookupAttribute"); // the repository's [Lookup]: a small, cached table, read whole on purpose
        var columns = new List<ColumnModel>();
        var navigations = new List<NavigationModel>();
        var collections = new List<CollectionModel>();
        var owned = new List<OwnedModel>();
        string? version = null;
        var identity = false;
        string? identityProperty = null;
        // The type's rules and its base types': a rule on the root of a hierarchy holds for Query<Derived>() and OfType<Derived>() too, as EF Core's query filters do.
        var rules = new List<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            rules.AddRange(current.GetAttributes().Where(a => a.AttributeClass is { Name: "VisibleWhenAttribute", TypeArguments.Length: 1 }).Select(a => a.AttributeClass!.TypeArguments[0]).OfType<INamedTypeSymbol>());
        var properties = MappedProperties(type, root);
        foreach (var property in properties)
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue;
            if (IsOwnedProperty(property))
            {
                // An owned type: its mapped members flattened into this table as {Property}_{Member}, built together when the row is read.
                var optional = IsNullable(property.Type);
                var ownedType = (INamedTypeSymbol)Unwrap(property.Type);
                var members = new List<ColumnModel>();
                foreach (var member in ownedType.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.GetMethod is not null && p.SetMethod is not null && p.DeclaredAccessibility == Accessibility.Public))
                {
                    if (member.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                        continue;
                    var flattened = TryColumn(member, ownedType, diagnostics, owner: property.Name, optionalOwner: optional);
                    if (flattened is null)
                        return null;
                    members.Add(flattened);
                    columns.Add(flattened);
                }

                if (members.Count == 0)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, type.Name, $"{property.Type.ToDisplayString()}, an [Owned] type with no mapped member"));
                    return null;
                }

                if (ConstructorMembers(ownedType, members) is not { } constructor)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, type.Name, $"{ownedType.Name}, an owned type with neither a public parameterless constructor nor one whose parameters are its members"));
                    return null;
                }

                owned.Add(new OwnedModel(property.Name, ownedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), optional, members) { Constructor = constructor });
                continue;
            }

            if (IsEntity(property.Type))
            {
                var key = properties.FirstOrDefault(p => p.Name == property.Name + "Id");
                // Towards a composite key: one {Navigation}{KeyProperty} per key property of the target, in the target's key order.
                var targetKeys = key is null ? ((INamedTypeSymbol)property.Type).GetMembers().OfType<IPropertySymbol>().Where(p => p.GetAttributes().Any(a => a.AttributeClass?.Name == "KeyAttribute")).ToList() : [];
                var composite = targetKeys.Count > 1 ? targetKeys.Select(k => properties.FirstOrDefault(p => p.Name == property.Name + k.Name)).ToList() : null;
                if (key is not null)
                    navigations.Add(new NavigationModel(property.Name, (INamedTypeSymbol)property.Type, key.Name, required: !IsNullable(key.Type)));
                else if (composite is not null && composite.All(p => p is not null))
                    navigations.Add(new NavigationModel(property.Name, (INamedTypeSymbol)property.Type, composite[0]!.Name, required: composite.All(p => !IsNullable(p!.Type)), composite.Select(p => p!.Name).ToArray()));
                else
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NavigationWithoutKey, property.Locations.FirstOrDefault(), type.Name, property.Name, property.Type.Name));
                continue;
            }

            if (IsCollectionOfEntities(property.Type))
            {
                // A one-to-many side: the child's foreign key is `<Parent>Id` by convention, as in the repository's entities.
                var child = (INamedTypeSymbol)((INamedTypeSymbol)property.Type).TypeArguments[0];
                if (property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ManyToManyAttribute") is { ConstructorArguments.Length: 3 } link)
                {
                    // A many-to-many side: the join table's two columns, no foreign key on the child.
                    collections.Add(new CollectionModel(property.Name, child, (string)link.ConstructorArguments[1].Value!, (string)link.ConstructorArguments[0].Value!, (string)link.ConstructorArguments[2].Value!));
                    continue;
                }

                // A Pragmatic entity's many-to-many: Persistence's shape declares the collection, the relation names the join table.
                if (PragmaticShape.IsPragmaticEntity(type) && PragmaticRelations.ManyToMany(type, property.Name, child) is { } pragmaticLink)
                {
                    collections.Add(new CollectionModel(property.Name, child, pragmaticLink.ThisKey, pragmaticLink.Table, pragmaticLink.OtherKey));
                    continue;
                }

                if (CollectionForeignKeys(type, child) is { } foreignKeys) // {Parent}Id, or one per key column of a composite parent
                    collections.Add(new CollectionModel(property.Name, child, foreignKeys[0]) { ForeignKeys = foreignKeys });
                else
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NavigationWithoutKey, property.Locations.FirstOrDefault(), type.Name, property.Name, child.Name));
                continue;
            }

            var plain = TryColumn(property, type, diagnostics, owner: null, optionalOwner: false);
            if (plain is null)
                return null;
            if (property.SetMethod!.DeclaredAccessibility != Accessibility.Public)
                (plain.PrivateSetter, plain.SetterOwner) = (true, property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            columns.Add(plain);
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "VersionAttribute") || plain.IsRowVersion)
                version = property.Name;
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "IdentityAttribute"))
            {
                identity = true;
                identityProperty = property.Name;
            }
        }

        List<DerivedModel>? derived = null;
        string? fixedPredicate = null;
        string? discriminatorPredicate = null;
        if (root is not null && !AddHierarchy(type, root, entities, compilation, columns, diagnostics, out derived, out fixedPredicate, out discriminatorPredicate))
            return null;
        if (schema is not null && schema.HasTable(table))
        {
            // The snapshot knows the table: its indexes decide, the attribute is not read (a stale [Indexed] would hide a scan).
            foreach (var column in columns)
                column.IsIndexed = column.IsKey || (column.Owner is null && column.Property == "Id") || schema.IsIndexed(table, column.Column);
        }

        var tpt = root is not null && IsTpt(root); // table per type: read through the view, written to the root's table and the type's own
        var model = new EntityModel(type, tpt ? TptView(table) : table, columns, navigations, collections, rules, version, identity, owned, derived, fixedPredicate) { ReadOnly = readOnly, Small = small, Tpc = tpc, DiscriminatorPredicate = discriminatorPredicate, Aliases = AliasesOf(type, columns) };
        if (tpt)
            (model.Tpt, model.WriteTable, model.PartTable) = (true, table, SymbolEqualityComparer.Default.Equals(type, root) ? null : OwnTable(type));
        model.FindRollUpParents(type, entities);
        model.MarkFullText(); // [FullText] columns: the entity's table and key, which the native full-text entries name
        if (!model.ReadStamps(diagnostics)) // [SoftDelete], [Auditable]: the columns the writer fills
            return null;
        if (compilation is not null)
        {
            model.ReadComputedScopes(compilation, diagnostics); // a scoped Pragmatic entity's DataScopeRule<T>s, in its scopes' OR
            model.ReadParentVisibility(entities, compilation); // a trait's child: its parent's owner and scopes
        }
        if (identity && (model.Keys.Count != 1 || model.Keys[0].Property != identityProperty))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.CompositeKey, type.Locations.FirstOrDefault(), type.Name, model.KeyList, "[Identity] goes on the one key column the database assigns"));
            return null;
        }

        if (model.Keys.Count == 0 && !readOnly)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.EntityWithoutKey, type.Locations.FirstOrDefault(), type.Name)); // its writer would name no row
            return null;
        }

        return model;
    }

    /// <summary>
    ///     One property as a column: its reader (through a <c>[Convert]</c> converter when it has one), its name, its key and cast marks.
    ///     An owned type's member gets <paramref name="owner" />: the column is <c>Owner_Member</c>, the property path <c>Owner.Member</c>,
    ///     and it admits null when the owner is optional. Null with a diagnostic when the type is not mapped.
    /// </summary>
    private static ColumnModel? TryColumn(IPropertySymbol property, INamedTypeSymbol declaring, List<Diagnostic> diagnostics, string? owner, bool optionalOwner)
    {
        // A converter ([Convert<TConverter>]) stores the property as the converter's store type: the reader and the binders go through its two functions.
        string? converter = null;
        ITypeSymbol store = property.Type;
        var convert = property.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "ConvertAttribute", TypeArguments.Length: 1 });
        if (convert is not null)
        {
            var converterType = convert.AttributeClass!.TypeArguments[0];
            var contract = converterType.AllInterfaces.FirstOrDefault(i => i.Name == "ISqlConverter" && i.TypeArguments.Length == 2);
            if (contract is null || !SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], Unwrap(property.Type)))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, declaring.Name, $"{property.Type.ToDisplayString()} through {converterType.Name}, which is not an ISqlConverter<{Unwrap(property.Type).ToDisplayString()}, TStore>"));
                return null;
            }

            converter = converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            store = contract.TypeArguments[1];
        }

        // A [ConcurrencyAware] Pragmatic entity's RowVersion (the shape's ulong): stored as a bigint, the providers having no unsigned 64-bit type.
        var rowVersion = owner is null && IsRowVersionProperty(property);
        if (rowVersion)
        {
            converter = "global::Pragmatic.Persistence.Sql.Conversion.SqlRowVersion";
            store = property.Type.ContainingAssembly.GetTypeByMetadataName("System.Int64")!; // ulong's assembly is the core library's: long is there
        }

        // An optional owner's members keep their own readers: the owner is built only when its first column is not null (see Members).
        var reader = converter is null ? ReaderFor(property.Type) : ConvertedReader(converter, store, IsNullable(property.Type));
        if (reader is null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, declaring.Name, converter is null ? property.Type.ToDisplayString() : $"{store.ToDisplayString()} (the converter's store type)"));
            return null;
        }

        // [Interned]: a string column read through a pool of its distinct values (one static pool per column, emitted with the maps).
        string? internPool = null;
        string? internKey = null;
        if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "InternedAttribute"))
        {
            if (converter is not null || Unwrap(property.Type).SpecialType != SpecialType.System_String)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, declaring.Name, $"{property.Type.ToDisplayString()} marked [Interned]: the pool holds strings read as they are (no converter)"));
                return null;
            }

            internKey = declaring.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString() + "." + property.Name;
            internPool = "Interned_" + System.Text.RegularExpressions.Regex.Replace(internKey, "[^A-Za-z0-9_]", "_");
            reader = $"global::Pragmatic.Persistence.Sql.Planning.SqlValue.{(IsNullable(property.Type) ? "NullableInterned" : "Interned")}({{r}}, {{i}}, {{d}}, EntityMaps.{internPool})";
        }

        var columnAttribute = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ColumnAttribute");
        var column = columnAttribute is { ConstructorArguments.Length: > 0 } && columnAttribute.ConstructorArguments[0].Value is string columnName ? columnName : property.Name;
        var cast = columnAttribute?.NamedArguments.FirstOrDefault(n => n.Key == "Cast").Value.Value as string;
        // [Vector(n)]: the dimensions for the casts the distances need; on PostgreSQL the JSON text parameter is cast to vector on writes.
        var vector = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "VectorAttribute");
        var dimensions = vector is { ConstructorArguments.Length: 1 } && vector.ConstructorArguments[0].Value is int n ? n : (int?)null;
        if (dimensions is not null)
        {
            if (property.Type is not IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Single })
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, declaring.Name, $"{property.Type.ToDisplayString()} marked [Vector]: a vector is a float[]"));
                return null;
            }

            cast ??= "vector";
        }
        var isKey = owner is null && property.GetAttributes().Any(a => a.AttributeClass?.Name == "KeyAttribute");
        // Indexed: the key (by attribute or the Id convention) or [Indexed], the stand-in where no schema snapshot lists the table (Read overrides it from the snapshot).
        var indexed = isKey || (owner is null && property.Name == "Id") || property.GetAttributes().Any(a => a.AttributeClass?.Name == "IndexedAttribute");
        // [DatabaseGenerated]: the database fills the column (a default, a computed column, a trigger); 2 when it changes on updates too (OnUpdate).
        var generated = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "DatabaseGeneratedAttribute") is { } mark ? mark.NamedArguments.Any(a => a.Key == "OnUpdate" && a.Value.Value is true) ? 2 : 1 : 0;
        var rollUp = owner is null ? RollUpOf(property) : null; // [RollUp<TChild>]: kept by the child's writes, filled by the database as far as this entity's go
        if (rollUp is not null && RollUpProblem(declaring, property, rollUp.Value.Child, rollUp.Value.Property) is { } problem)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, declaring.Name, problem));
            return null;
        }

        return owner is null
            ? new ColumnModel(property.Name, column, property.Type, reader, isKey, converter, store, cast) { IsIndexed = indexed, InternPool = internPool, InternKey = internKey, VectorDimensions = dimensions, Generated = isKey ? 0 : rollUp is not null ? 1 : generated, RollUpChild = rollUp?.Child, RollUpProperty = rollUp?.Property, FullText = property.GetAttributes().Any(a => a.AttributeClass?.Name == "FullTextAttribute"), IsRowVersion = rowVersion }
            : new ColumnModel($"{owner}.{property.Name}", $"{owner}_{column}", property.Type, reader, false, converter, store, cast, owner, property.Name, optionalOwner) { IsIndexed = indexed, InternPool = internPool, InternKey = internKey, VectorDimensions = dimensions };
    }

    /// <summary>The <c>ulong RowVersion</c> of a Pragmatic <c>[ConcurrencyAware]</c> entity (on it or a base type), as the shape declares it.</summary>
    private static bool IsRowVersionProperty(IPropertySymbol property)
    {
        if (property.Name != "RowVersion" || property.Type.SpecialType != SpecialType.System_UInt64)
            return false;
        for (var type = property.ContainingType; type is not null; type = type.BaseType)
        {
            if (PragmaticShape.Has(type, "ConcurrencyAwareAttribute"))
                return true;
        }

        return false;
    }

    private static bool IsCollectionOfEntities(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic && IsEntity(generic.TypeArguments[0]);
}

