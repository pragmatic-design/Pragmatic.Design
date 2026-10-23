using Pragmatic.Persistence.Sql;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>An <c>[Owned]</c> type flattened into an entity's table: the property that holds it, whether it may be null, and its members' columns.</summary>
internal sealed class OwnedModel
{
    public OwnedModel(string property, string typeName, bool optional, List<ColumnModel> columns)
    {
        Property = property;
        TypeName = typeName;
        Optional = optional;
        Columns = columns;
    }

    public string Property { get; }

    /// <summary>The owned type, fully qualified.</summary>
    public string TypeName { get; }

    /// <summary>True when the owner property is nullable: the columns are null together, the object null when they are.</summary>
    public bool Optional { get; }

    public List<ColumnModel> Columns { get; }

    /// <summary>The members its constructor takes, in order (a value object without a parameterless constructor); empty: built with an initializer.</summary>
    public List<string> Constructor { get; set; } = [];
}

/// <summary>A derived type of a hierarchy stored in the root's table: its name (the discriminator value) and its own columns.</summary>
internal sealed class DerivedModel
{
    public DerivedModel(INamedTypeSymbol type, List<ColumnModel> columns)
    {
        Type = type;
        Columns = columns;
    }

    public INamedTypeSymbol Type { get; }

    public string Name => Type.Name;

    public string FullName => Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public List<ColumnModel> Columns { get; }
}

internal sealed class ColumnModel
{
    public ColumnModel(string property, string column, ITypeSymbol type, string reader, bool isKey = false, string? converter = null, ITypeSymbol? store = null, string? cast = null, string? owner = null, string? member = null, bool optionalOwner = false)
    {
        Property = property;
        Column = column;
        Type = type;
        Reader = reader;
        IsKey = isKey;
        Converter = converter;
        Store = store ?? type;
        Cast = cast;
        Owner = owner;
        Member = member ?? property;
        OptionalOwner = optionalOwner;
        Access = owner is null ? property : $"{owner}{(optionalOwner ? "?" : "")}.{member}";
    }

    /// <summary>The discriminator of a hierarchy: no property behind it; <see cref="Value" /> is the type's name (a derived type's constant, or a switch on the row's type at the root).</summary>
    public bool IsDiscriminator { get; set; }

    /// <summary>True when the column is the key or carries <c>[Indexed]</c>: a WHERE on it is not a scan (PRAG3011 otherwise).</summary>
    public bool IsIndexed { get; set; }

    /// <summary>
    ///     The property's setter is not public (Pragmatic's generated <c>OwnerId</c>, <c>{Navigation}Id</c>, <c>AccessScopes</c>): the row is
    ///     built with the others in the initializer, this one through <c>EntityMaps.Set_{Type}_{Property}</c>, an <c>[UnsafeAccessor]</c>.
    /// </summary>
    public bool PrivateSetter { get; set; }

    /// <summary>The type that declares the property with the private setter (its accessor's target: a base type's private member is not the derived type's).</summary>
    public string? SetterOwner { get; set; }

    /// <summary>The accessor's helper in <c>EntityMaps</c>: sets the property and returns the row, so the row stays one expression.</summary>
    public string SetterName => "Set_" + SetterOwner!.Replace("global::", "").Replace('.', '_') + "_" + Property;

    /// <summary>A derived type's own column seen from the root: read only in that type's branch, bound as <c>(e as Derived)?.Prop</c>.</summary>
    public string? DerivedOf { get; set; }

    /// <summary>The C# text the discriminator binds (a literal, or a switch over the derived types); null for a plain column.</summary>
    public string? Discriminator { get; set; }

    /// <summary>The C# that reads this column's value from the entity variable <paramref name="entity" />, as the property holds it.</summary>
    public string Value(string entity)
        => IsDiscriminator ? Discriminator!.Replace("{e}", entity) : DerivedOf is not null ? $"({entity} as {DerivedOf})?.{Access}" : $"{entity}.{Access}";

    /// <summary>True for a member of an optional owned type: its column may be null although the member's type is not.</summary>
    public bool OptionalOwner { get; }

    /// <summary>The reader of this column projected on its own (not inside the owner's guarded initializer): nullable when an optional owner may leave it null; a converted or interned column keeps its own.</summary>
    public string? ProjectedReader(ITypeSymbol asType) => Converter is not null || (InternPool is not null && !OptionalOwner) ? Reader : EntityModel.ReaderFor(asType, OptionalOwner);

    /// <summary>The static pool field (in <c>EntityMaps</c>) an <c>[Interned]</c> column reads through; null for the others.</summary>
    public string? InternPool { get; set; }

    /// <summary>The pool's key across assemblies (<c>Namespace.Entity.Property</c>): every assembly reading the column shares one pool.</summary>
    public string? InternKey { get; set; }

    /// <summary><c>[DatabaseGenerated]</c>: 0 for a column the writes bind; 1 when the database fills it on inserts, 2 on updates too. Never written, read back by key.</summary>
    public int Generated { get; set; }

    /// <summary>A <c>[FullText]</c> column: marked by the reader of the property, its table and key filled in with the entity's (the native entries name them).</summary>
    public bool FullText { get; set; }

    public string? FullTextTable { get; set; }

    public string? FullTextKey { get; set; }

    /// <summary>A <c>[RollUp&lt;TChild&gt;]</c> column: the child type and the child property summed (null for a count); filled by the child's writes.</summary>
    public INamedTypeSymbol? RollUpChild { get; set; }

    public string? RollUpProperty { get; set; }

    /// <summary>The dimensions of a <c>[Vector(n)]</c> column; null for the others. The distance functions cast their vector argument to this size where the dialect wants it.</summary>
    public int? VectorDimensions { get; set; }

    /// <summary>
    ///     A <c>[ConcurrencyAware]</c> Pragmatic entity's <c>RowVersion</c>: the database's own version where it has one (PostgreSQL's <c>xmin</c>, SQL
    ///     Server's <c>rowversion</c>), a BIGINT the writes advance elsewhere; read as a bigint everywhere (<see cref="SqlDialects.RowVersion" />).
    /// </summary>
    public bool IsRowVersion { get; set; }

    /// <summary>The column in a select list under <paramref name="alias" />: the reference itself, or the token the dialect resolves (a vector read as text, a row version as a bigint).</summary>
    public string Select(string alias)
        => IsRowVersion ? SqlDialects.RowVersionSelect(alias, Column) : VectorDimensions is null ? $"{alias}.\"{Column}\"" : SqlDialects.VectorSelect($"{alias}.\"{Column}\"");

    /// <summary>Marked <c>[Key]</c>: one column of the entity's key.</summary>
    public bool IsKey { get; }

    /// <summary>The property, as a path for an owned type's member (<c>Address.City</c>).</summary>
    public string Property { get; }

    /// <summary>The owning property when the column is an owned type's member; null for the entity's own columns.</summary>
    public string? Owner { get; }

    /// <summary>The member's own name (<c>City</c>), the property's for a plain column.</summary>
    public string Member { get; }

    /// <summary>How the value is reached from the entity (<c>Address.City</c>, <c>Return?.City</c> when the owner is optional): what the binders read.</summary>
    public string Access { get; }

    public string Column { get; }

    /// <summary>The property's type.</summary>
    public ITypeSymbol Type { get; }

    /// <summary>The type the column stores: the converter's store type, or the property's.</summary>
    public ITypeSymbol Store { get; }

    /// <summary>The <c>ISqlConverter</c> between <see cref="Type" /> and <see cref="Store" />, fully qualified; null when the column stores the property's type.</summary>
    public string? Converter { get; }

    /// <summary>PostgreSQL: the type the bound parameter is cast to on writes (<c>[Column(Cast = "jsonb")]</c>).</summary>
    public string? Cast { get; }

    /// <summary>The reader template, with <c>{r}</c>, <c>{i}</c>, <c>{d}</c> to fill; it yields the property's type, converter applied.</summary>
    public string Reader { get; }

    public string Read(string reader, string ordinal, string dialect) => Reader.Replace("{r}", reader).Replace("{i}", ordinal).Replace("{d}", dialect);

    /// <summary>The C# that yields <paramref name="value" /> (an expression of the property's type) as the column stores it: through the converter, null staying null.</summary>
    public string Bind(string value) => Converter is null ? EntityModel.IsArrayColumn(Store) ? $"global::Pragmatic.Persistence.Sql.SqlArrays.ToStore(d, {value})" : value : Convert(Converter, value, DerivedOf is null ? Type : Type.WithNullableAnnotation(NullableAnnotation.Annotated), Store);

    /// <summary>A value of <paramref name="type" /> as <paramref name="store" /> through <paramref name="converter" />: <c>ToStore</c> directly, or the null-keeping helpers for a nullable value.</summary>
    public static string Convert(string converter, string value, ITypeSymbol type, ITypeSymbol store)
    {
        if (!EntityModel.IsNullable(type))
            return $"{converter}.ToStore({value})";
        var model = EntityModel.Unwrap(type).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var stored = EntityModel.Unwrap(store).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var helper = type.IsValueType ? "Converted" : "ConvertedRef";
        return $"global::Pragmatic.Persistence.Sql.SqlValue.{helper}<{model}, {stored}, {converter}>({value})";
    }
}

/// <summary>A piece of a fragment that differs by dialect: a catalog function rendered once per dialect, resolved by the plan where the statement is written.</summary>
internal sealed class DialectPiece
{
    public DialectPiece(string sqlite, string postgreSql, string sqlServer, string mySql, string oracle)
    {
        Sqlite = sqlite;
        PostgreSql = postgreSql;
        SqlServer = sqlServer;
        MySql = mySql;
        Oracle = oracle;
    }

    public string Oracle { get; }

    public string Sqlite { get; }

    public string PostgreSql { get; }

    public string SqlServer { get; }

    public string MySql { get; }

    public string For(SqlDialect dialect) => dialect switch { SqlDialect.Sqlite => Sqlite, SqlDialect.PostgreSql => PostgreSql, SqlDialect.MySql => MySql, SqlDialect.Oracle => Oracle, _ => SqlServer };
}

internal sealed class NavigationModel
{
    public NavigationModel(string property, INamedTypeSymbol target, string foreignKey, bool required, string[]? foreignKeys = null)
    {
        Property = property;
        Target = target;
        ForeignKey = foreignKey;
        Required = required;
        ForeignKeys = foreignKeys ?? [foreignKey];
    }

    public string Property { get; }

    public INamedTypeSymbol Target { get; }

    /// <summary>The foreign key property; the first of <see cref="ForeignKeys" /> towards a composite key.</summary>
    public string ForeignKey { get; }

    /// <summary>The foreign key properties, one per key column of the target in its order (<c>{Navigation}{KeyProperty}</c> towards a composite key).</summary>
    public string[] ForeignKeys { get; }

    /// <summary>True when the navigation joins on several columns: the typed chains follow it, the runtime path does not.</summary>
    public bool IsComposite => ForeignKeys.Length > 1;

    /// <summary>True when the foreign key property cannot be null: the navigation always has its target, and joins INNER.</summary>
    public bool Required { get; }

    public string Alias => "j_" + Property;
}

/// <summary>
///     A one-to-many side: the collection property, the child entity, and the child's foreign key property back to the parent. Or a
///     many-to-many side (<c>[ManyToMany]</c>): the join table, its column holding the parent's key (<see cref="ForeignKey" />, a column
///     name there, not a property) and its column holding the child's.
/// </summary>
internal sealed class CollectionModel
{
    public CollectionModel(string property, INamedTypeSymbol child, string foreignKey, string? linkTable = null, string? linkChildColumn = null)
    {
        Property = property;
        Child = child;
        // A many-to-many side names a composite key's join-table columns comma-separated ("OrderId,Tag"), in the key's order.
        ForeignKeys = linkTable is null ? [foreignKey] : foreignKey.Split(',').Select(c => c.Trim()).ToArray();
        ForeignKey = ForeignKeys[0];
        LinkTable = linkTable;
        LinkChildColumns = linkChildColumn is null ? [] : linkChildColumn.Split(',').Select(c => c.Trim()).ToArray();
        LinkChildColumn = linkChildColumn is null ? null : LinkChildColumns[0];
    }

    public string Property { get; }

    public INamedTypeSymbol Child { get; }

    public string ForeignKey { get; }

    /// <summary>The child's foreign key properties, one per key column of the parent in its order; <see cref="ForeignKey" /> alone for a single-column key.</summary>
    public string[] ForeignKeys { get; set; }

    public bool IsComposite => ForeignKeys.Length > 1;

    /// <summary>The child's columns holding the parent's key, in the parent's key order.</summary>
    public string[] ForeignKeyColumns(EntityModel child) => IsLink ? ForeignKeys : IsComposite ? ForeignKeys.Select(fk => child.Column(fk)?.Column ?? fk).ToArray() : [ForeignKeyColumn(child)];

    /// <summary>The child rows under <paramref name="childAlias" /> whose foreign key is the parent's key under <paramref name="parentAlias" />: one equality per key column.</summary>
    public string Matches(EntityModel parent, string parentAlias, EntityModel child, string childAlias)
        => string.Join(" AND ", ForeignKeyColumns(child).Select((fk, i) => $"{childAlias}.\"{fk}\" = {parentAlias}.\"{(IsComposite ? parent.Keys[i].Column : parent.KeyColumn)}\""));

    public string? LinkTable { get; }

    public string? LinkChildColumn { get; }

    /// <summary>The join table's columns holding the child's key, one per key column of a composite child.</summary>
    public string[] LinkChildColumns { get; }

    public bool IsLink => LinkTable is not null;

    /// <summary>A many-to-many side whose join table does not hold one column per key column of each side: the reason; null when it does (or for a one-to-many).</summary>
    public string? LinkProblem(EntityModel parent, EntityModel child)
    {
        if (!IsLink)
            return parent.HasCompositeKey && !IsComposite ? $"{parent.Type.Name} has a composite key ({parent.KeyList}): {Child.Name} carries no foreign key per key column" : null;
        if (ForeignKeys.Length != parent.Keys.Count)
            return $"the join table \"{LinkTable}\" holds {ForeignKeys.Length} column(s) for {parent.Type.Name}'s key ({parent.KeyList}): [ManyToMany] names one per key column, comma-separated";
        return LinkChildColumns.Length != child.Keys.Count
            ? $"the join table \"{LinkTable}\" holds {LinkChildColumns.Length} column(s) for {child.Type.Name}'s key ({child.KeyList}): [ManyToMany] names one per key column, comma-separated"
            : null;
    }

    /// <summary>The child's column holding the parent's key (the join table's, for a many-to-many side).</summary>
    public string ForeignKeyColumn(EntityModel child) => IsLink ? ForeignKey : child.Column(ForeignKey)?.Column ?? ForeignKey;

    /// <summary>The INNER JOIN of the join table to the child rows under <paramref name="childAlias" />, the link under <paramref name="linkAlias" />; null for a one-to-many.</summary>
    public string? LinkJoin(EntityModel child, string childAlias, string linkAlias)
        => IsLink ? $"INNER JOIN \"{LinkTable}\" {linkAlias} ON " + string.Join(" AND ", LinkChildColumns.Select((c, i) => $"{linkAlias}.\"{c}\" = {childAlias}.\"{(i < child.Keys.Count ? child.Keys[i].Column : child.KeyColumn)}\"")) : null;
}
