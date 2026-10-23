using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A hierarchy in one table (<c>[Inheritance]</c> on the root), at any depth: the root's model carries a discriminator column and the
///     own columns of every type below it, read in a switch on the discriminator (each branch with the columns of its type and of the types
///     between it and the root); a leaf's model carries the columns above it, the discriminator as its constant, its own columns, and a
///     fixed predicate selecting its rows; an intermediate type reads like a root of its own subtree, its predicate an IN over its types.
///     A table per concrete type has one level.
/// </summary>
internal sealed partial class EntityModel
{
    public bool IsHierarchyRoot => Derived is not null;

    /// <summary>The type with <c>[Inheritance]</c> this one is, or derives from; null outside a hierarchy.</summary>
    private static INamedTypeSymbol? HierarchyRootOf(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.Name == "InheritanceAttribute"))
                return current;
        }

        return null;
    }

    /// <summary>The mapped properties of a type, the inherited ones first: a derived type's model lists the root's columns before its own.</summary>
    private static List<IPropertySymbol> MappedProperties(INamedTypeSymbol type, INamedTypeSymbol? root)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            chain.Insert(0, current);
            if (SymbolEqualityComparer.Default.Equals(current, root))
                break;
        }

        return chain.SelectMany(OwnProperties).ToList();
    }

    private static IEnumerable<IPropertySymbol> OwnProperties(INamedTypeSymbol type)
        => type.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.GetMethod is not null && p.SetMethod is not null && p.DeclaredAccessibility == Accessibility.Public);

    /// <summary>True in a table-per-type hierarchy: read through the view <see cref="Table" />, written to <see cref="WriteTable" /> and <see cref="PartTable" />.</summary>
    public bool Tpt { get; set; }

    /// <summary>Table per type: the root's own table, where the root's columns of every row are written.</summary>
    public string? WriteTable { get; set; }

    /// <summary>Table per type: a derived type's own table (its key and its own columns); null for the root.</summary>
    public string? PartTable { get; set; }

    /// <summary>The view a table-per-type hierarchy reads through: the root's table and <c>View</c>.</summary>
    public static string TptView(string rootTable) => rootTable + "View";

    /// <summary>True when the root's <c>[Inheritance]</c> says table per type.</summary>
    private static bool IsTpt(INamedTypeSymbol root) => StrategyOf(root) == "Tpt";

    /// <summary>
    ///     The root's strategy as the enum member's name (Tph, Tpt, Tpc), from the constructor (the repository's form) or the named argument:
    ///     the spike's enum and the repository's number the members differently. Tph when none is given.
    /// </summary>
    private static string StrategyOf(INamedTypeSymbol root)
    {
        var attribute = root.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "InheritanceAttribute");
        if (attribute is null)
            return "Tph";
        var strategy = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0] : attribute.NamedArguments.FirstOrDefault(n => n.Key == "Strategy").Value;
        if (strategy.Kind != TypedConstantKind.Enum || strategy.Type is not INamedTypeSymbol enumType)
            return "Tph";
        return enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, strategy.Value))?.Name ?? "Tph";
    }

    /// <summary>A type's own [Table], or its pluralized name.</summary>
    private static string OwnTable(INamedTypeSymbol type)
        => type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "TableAttribute") is { ConstructorArguments.Length: > 0 } table && table.ConstructorArguments[0].Value is string named ? named : Pluralize(type.Name);

    /// <summary>True when the root's <c>[Inheritance]</c> says table per concrete type.</summary>
    private static bool IsTpc(INamedTypeSymbol root) => StrategyOf(root) == "Tpc";

    /// <summary>
    ///     Adds the hierarchy's columns to <paramref name="columns" />: at the root the discriminator (its value a switch on the row's type)
    ///     and each derived type's own columns; at a derived type in one table the discriminator as the type's name, and the predicate on it.
    /// </summary>
    private static bool AddHierarchy(INamedTypeSymbol type, INamedTypeSymbol root, IReadOnlyList<INamedTypeSymbol>? entities, Compilation? compilation, List<ColumnModel> columns, List<Diagnostic> diagnostics, out List<DerivedModel>? derived, out string? fixedPredicate, out string? discriminatorPredicate)
    {
        derived = null;
        fixedPredicate = null;
        discriminatorPredicate = null;
        var attribute = root.GetAttributes().First(a => a.AttributeClass?.Name == "InheritanceAttribute");
        var discriminator = attribute.NamedArguments.FirstOrDefault(n => n.Key == "DiscriminatorColumn").Value.Value as string // the repository's name
                            ?? attribute.NamedArguments.FirstOrDefault(n => n.Key == "Discriminator").Value.Value as string ?? "Discriminator";
        var tpc = IsTpc(root);
        var stringType = compilation?.GetSpecialType(SpecialType.System_String) ?? throw new InvalidOperationException("a hierarchy needs the compilation to type its discriminator");
        const string reader = "global::Pragmatic.Persistence.Sql.SqlValue.String({r}, {i})";
        var isRoot = SymbolEqualityComparer.Default.Equals(type, root);
        // Every [Entity] below this type, at any depth (a table per concrete type: one level only, its view unions the concrete tables).
        _ = entities;
        var derivedTypes = DerivedTypesOf(type).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        if (tpc && derivedTypes.Any(d => !SymbolEqualityComparer.Default.Equals(d.BaseType, root)))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.InheritanceShape, type.Locations.FirstOrDefault(), type.Name, "a table-per-concrete-type hierarchy has one level of derived types (the root's view unions their tables)"));
            return false;
        }

        if (!isRoot)
        {
            // A derived type: the columns of the types above it are already in (MappedProperties). In one table its rows are its own name and its
            // descendants' (an intermediate type reads those as their own types, below); in a table per type there is no discriminator column (the
            // root's view adds the literal).
            var names = (type.IsAbstract ? [] : new[] { type.Name }).Concat(tpc ? [] : derivedTypes.Select(d => d.Name)).ToList();
            discriminatorPredicate = names.Count == 1 ? $"t.\"{discriminator}\" = '{names[0]}'" : $"t.\"{discriminator}\" IN ({string.Join(", ", names.Select(n => $"'{n}'"))})";
            if (tpc)
                return true;
            fixedPredicate = discriminatorPredicate;
            if (derivedTypes.Count == 0)
            {
                var insertAt = columns.Count - OwnColumnCount(columns, type);
                columns.Insert(insertAt, new ColumnModel("$Discriminator", discriminator, stringType, reader) { IsDiscriminator = true, Discriminator = SqlInterceptorGenerator.Literal(type.Name) });
                return true;
            }
        }

        // The root, or an intermediate type with types below it: the discriminator after the columns so far (a switch on the row's type, the deepest
        // types first), then the own columns of every type below (nullable in the table, read in the branches of the types that have them).
        // The derived types live in the root's assembly (ASSUMPTION), so a consuming assembly's generator sees the same hierarchy as the declaring one.
        var cases = string.Join(", ", derivedTypes.OrderByDescending(d => Depth(d, type)).ThenBy(d => d.Name, StringComparer.Ordinal).Select(d => $"{d.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} => {SqlInterceptorGenerator.Literal(d.Name)}"));
        var fallback = type.IsAbstract ? $"throw new global::System.InvalidOperationException(\"{type.Name} is abstract: the row's type is one of its derived types\")" : SqlInterceptorGenerator.Literal(type.Name);
        columns.Add(new ColumnModel("$Discriminator", discriminator, stringType, reader) { IsDiscriminator = true, Discriminator = $"({{e}} switch {{ {cases}{(cases.Length > 0 ? ", " : "")}_ => {fallback} }})" });
        derived = [];
        var declared = new Dictionary<INamedTypeSymbol, List<ColumnModel>>(SymbolEqualityComparer.Default); // each type's own columns, once
        foreach (var derivedType in derivedTypes)
        {
            var lineage = new List<ColumnModel>();
            for (var level = derivedType; level is not null && !SymbolEqualityComparer.Default.Equals(level, type); level = level.BaseType)
            {
                if (!declared.TryGetValue(level, out var levelColumns))
                {
                    levelColumns = OwnColumns(level, columns, diagnostics);
                    if (levelColumns is null)
                        return false;
                    declared[level] = levelColumns;
                }

                lineage.AddRange(levelColumns);
            }

            derived.Add(new DerivedModel(derivedType, lineage));
        }

        return true;
    }

    /// <summary>True when <paramref name="ancestor" /> is among <paramref name="type" />'s base types.</summary>
    internal static bool Below(INamedTypeSymbol type, INamedTypeSymbol ancestor)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>How many levels <paramref name="type" /> sits below <paramref name="ancestor" />.</summary>
    private static int Depth(INamedTypeSymbol type, INamedTypeSymbol ancestor)
    {
        var depth = 0;
        for (var current = type; current is not null && !SymbolEqualityComparer.Default.Equals(current, ancestor); current = current.BaseType)
            depth++;
        return depth;
    }

    /// <summary>A type's own columns seen from the type above it (read as <c>(e as Type)?.Prop</c>), appended to <paramref name="columns" />; null with a diagnostic.</summary>
    private static List<ColumnModel>? OwnColumns(INamedTypeSymbol derivedType, List<ColumnModel> columns, List<Diagnostic> diagnostics)
    {
        var own = new List<ColumnModel>();
        foreach (var property in OwnProperties(derivedType))
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue;
            if (IsEntity(property.Type) || IsCollectionOfEntities(property.Type))
                continue; // a derived type's own navigation: in its own model, reached from the root through a cast (((Derived)x).Parent)
            if (IsOwnedProperty(property))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property.Locations.FirstOrDefault(), property.Name, derivedType.Name, $"{property.Type.ToDisplayString()}: a derived type's own members are plain columns or navigations (owned types go on the root)"));
                return null;
            }

            var column = TryColumn(property, derivedType, diagnostics, owner: null, optionalOwner: false);
            if (column is null)
                return null;
            var seenFromRoot = new ColumnModel(column.Property, column.Column, column.Type, column.Reader, false, column.Converter, column.Store, column.Cast) { DerivedOf = derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) };
            own.Add(seenFromRoot);
            columns.Add(seenFromRoot);
        }

        return own;
    }

    /// <summary>The [Entity] classes of the root's assembly below <paramref name="ancestor" />, at any depth, wherever their namespace.</summary>
    private static IEnumerable<INamedTypeSymbol> DerivedTypesOf(INamedTypeSymbol ancestor)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(ancestor.ContainingAssembly.GlobalNamespace);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol ns:
                        pending.Push(ns);
                        break;
                    case INamedTypeSymbol type:
                        if (IsEntity(type) && Below(type, ancestor))
                            yield return type;
                        pending.Push(type); // nested types too
                        break;
                }
            }
        }
    }

    /// <summary>How many of the columns at the end belong to <paramref name="type" />'s own properties (a derived type's, after the root's).</summary>
    private static int OwnColumnCount(List<ColumnModel> columns, INamedTypeSymbol type)
    {
        var own = new HashSet<string>(OwnProperties(type).Select(p => p.Name), StringComparer.Ordinal);
        var count = 0;
        for (var i = columns.Count - 1; i >= 0 && own.Contains(columns[i].Owner ?? columns[i].Property); i--)
            count++;
        return count;
    }

    /// <summary>
    ///     The C# that builds the row: <c>new T { members }</c>, or at a hierarchy's root a switch on the discriminator building each derived
    ///     type with the root's members and its own (<paramref name="extra" /> members, the to-one includes, go into every branch).
    /// </summary>
    public string Construct(string reader, string dialect, Func<int, string> ordinal, string? extra = null)
    {
        if (Derived is null)
            return Built(FullName, reader, dialect, ordinal, null, extra);
        var discriminator = Columns.FindIndex(c => c.IsDiscriminator);
        var branches = new List<string>();
        foreach (var derived in Derived)
            branches.Add($"{SqlInterceptorGenerator.Literal(derived.Name)} => {Built(derived.FullName, reader, dialect, ordinal, c => c.DerivedOf is null || derived.Columns.Contains(c), extra)}");
        branches.Add(Type.IsAbstract
            ? $"_ => throw new global::System.InvalidOperationException(\"unknown {Columns[discriminator].Column} value for {Type.Name}\")"
            : $"_ => {Built(FullName, reader, dialect, ordinal, c => c.DerivedOf is null, extra)}");
        return $"({Columns[discriminator].Read(reader, ordinal(discriminator), dialect)} switch {{ {string.Join(", ", branches)} }})";
    }

    /// <summary>
    ///     One type's row: the columns with a public setter (and <paramref name="extra" />) in the initializer, each column with a private one
    ///     set through its accessor around it, <c>EntityMaps.Set_T_P(new T { … }, value)</c>: still one expression.
    /// </summary>
    private string Built(string fullName, string reader, string dialect, Func<int, string> ordinal, Func<ColumnModel, bool>? subset, string? extra)
    {
        var parts = new List<string>();
        var members = Members(reader, dialect, ordinal, c => !c.PrivateSetter && (subset is null || subset(c)));
        if (members.Length > 0)
            parts.Add(members);
        if (!string.IsNullOrEmpty(extra))
            parts.Add(extra!);
        var text = $"new {fullName} {{ {string.Join(", ", parts)} }}";
        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];
            if (column.PrivateSetter && column.Owner is null && !column.IsDiscriminator && (subset is null || subset(column)))
                text = $"EntityMaps.{column.SetterName}({text}, {column.Read(reader, ordinal(i), dialect)})";
        }

        return text;
    }
}
