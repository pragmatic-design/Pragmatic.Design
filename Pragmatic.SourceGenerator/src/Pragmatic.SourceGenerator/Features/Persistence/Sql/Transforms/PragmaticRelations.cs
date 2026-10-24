using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     What the Sql engine reads off Pragmatic's <c>[Relation.*]</c> declarations beyond the members Persistence's shape declares: a
///     many-to-many's join table, a tree's self-reference, the delete behavior of a foreign key.
/// </summary>
internal static class PragmaticRelations
{
    /// <summary>One <c>[Relation.X&lt;T&gt;]</c> as declared (a class, not a record: the generator compiles for netstandard2.0).</summary>
    private sealed class Declared(string kind, INamedTypeSymbol owner, INamedTypeSymbol target, string? name, string? inverse, string? foreignKey, bool required, bool isPrincipal, string? joinTable, string? onDelete)
    {
        public string? OnDelete { get; } = onDelete;

        public string Kind { get; } = kind;
        public INamedTypeSymbol Owner { get; } = owner;
        public INamedTypeSymbol Target { get; } = target;
        public string? Name { get; } = name;
        public string? Inverse { get; } = inverse;
        public string? ForeignKey { get; } = foreignKey;
        public bool Required { get; } = required;
        public bool IsPrincipal { get; } = isPrincipal;
        public string? JoinTable { get; } = joinTable;
    }

    /// <summary>
    ///     The join table and columns of <paramref name="entity" />'s many-to-many collection <paramref name="property" /> of
    ///     <paramref name="other" />, as Pragmatic names them (<c>JoinTableNaming</c>, <c>JoinColumnNaming</c>): the table the relation
    ///     says, else the two names in ordinal order; the columns each side's name and <c>Id</c>. Null when no
    ///     <c>[Relation.ManyToMany&lt;T&gt;]</c> on either side makes that collection.
    /// </summary>
    /// <remarks>
    ///     The collection is declared by Persistence's shape, which names no join table: the Sql engine reads it from the
    ///     relation, as EF Core's configuration does. A collection declared on one side gets its inverse on the other unless that
    ///     side declares the relation itself.
    /// </remarks>
    public static (string Table, string ThisKey, string OtherKey)? ManyToMany(INamedTypeSymbol entity, string property, INamedTypeSymbol other)
    {
        var all = Declarations(entity).Concat(SymbolEqualityComparer.Default.Equals(entity, other) ? [] : Declarations(other)).ToList();
        foreach (var relation in all.Where(d => d.Kind == "ManyToMany"))
        {
            var owner = relation.Owner;
            var target = relation.Target;
            var name = relation.Name ?? PragmaticNames.Pluralize(target.Name);
            var table = relation.JoinTable ?? (string.CompareOrdinal(owner.Name, target.Name) <= 0 ? owner.Name + target.Name : target.Name + owner.Name);
            var (left, right) = owner.Name != target.Name ? (owner.Name + "Id", target.Name + "Id") : ((relation.Inverse ?? "Inverse") + "Id", name + "Id");
            if (SymbolEqualityComparer.Default.Equals(owner, entity) && SymbolEqualityComparer.Default.Equals(target, other) && name == property)
                return (table, left, right);
            if (SymbolEqualityComparer.Default.Equals(owner, other) && SymbolEqualityComparer.Default.Equals(target, entity)
                && (relation.Inverse ?? PragmaticNames.Pluralize(owner.Name)) == property && Counterpart(all, entity, other, "ManyToMany", null) is null)
                return (table, right, left);
        }

        return null;
    }

    /// <summary>The other side's declaration of the same relation (by its name when the inverse is named), if it declares one.</summary>
    private static Declared? Counterpart(List<Declared> all, INamedTypeSymbol owner, INamedTypeSymbol target, string kind, string? inverse)
    {
        var candidates = all.Where(d => d.Kind == kind && SymbolEqualityComparer.Default.Equals(d.Owner, owner) && SymbolEqualityComparer.Default.Equals(d.Target, target)).ToList();
        return candidates.Count <= 1 || inverse is null ? candidates.FirstOrDefault() : candidates.FirstOrDefault(c => (c.Name ?? c.Target.Name) == inverse) ?? candidates[0];
    }

    /// <summary>
    ///     The trees of an entity (<c>[GenerateHierarchy(Via = …)]</c>): each a self-referencing ManyToOne, its navigation's name and parent key
    ///     property (<c>{Navigation}Id</c> unless the relation names one). Via picks the navigation; without it, the one self-reference.
    /// </summary>
    public static IEnumerable<(string Navigation, string ForeignKey)> Trees(INamedTypeSymbol entity)
    {
        var selves = Declarations(entity).Where(d => d.Kind == "ManyToOne" && SymbolEqualityComparer.Default.Equals(d.Target, entity)).ToList();
        foreach (var attribute in entity.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Entity.GenerateHierarchyAttribute"))
        {
            var via = attribute.NamedArguments.FirstOrDefault(n => n.Key == "Via").Value.Value as string;
            var tree = via is null ? (selves.Count == 1 ? selves[0] : null) : selves.FirstOrDefault(s => (s.Name ?? s.Target.Name) == via);
            if (tree is not null)
                yield return (tree.Name ?? tree.Target.Name, tree.ForeignKey ?? (tree.Name ?? tree.Target.Name) + "Id");
        }
    }

    /// <summary>
    ///     What deleting <paramref name="target" /> does to <paramref name="dependent" />'s rows along <paramref name="navigation" />, as Pragmatic's
    ///     <c>DeleteBehavior</c> name: the dependent's ManyToOne/OneToOne declaration (Restrict unless it says), else the target's OneToMany of the
    ///     dependent (Cascade unless it says), else NoAction. ASSUMPTION: when both sides declare, the dependent's holds (Pragmatic's
    ///     <c>RelationPairing.Resolve</c> is not reproduced; no entity of the spike's suites declares both).
    /// </summary>
    public static string OnDelete(INamedTypeSymbol dependent, string navigation, INamedTypeSymbol target)
    {
        var own = Declarations(dependent).FirstOrDefault(d => d.Kind is "ManyToOne" or "OneToOne" && SymbolEqualityComparer.Default.Equals(d.Target, target) && (d.Name ?? d.Target.Name) == navigation);
        if (own is not null)
            return own.OnDelete ?? "Restrict";
        var parent = Declarations(target).FirstOrDefault(d => d.Kind == "OneToMany" && SymbolEqualityComparer.Default.Equals(d.Target, dependent) && (d.Inverse ?? target.Name) == navigation);
        return parent is null ? "NoAction" : parent.OnDelete ?? "Cascade";
    }

    /// <summary>The <c>[Relation.X&lt;T&gt;]</c> and <c>[Relation.X&lt;T&gt;.WithNavigation(…)]</c> of an entity.</summary>
    private static IEnumerable<Declared> Declarations(INamedTypeSymbol entity)
    {
        foreach (var attribute in entity.GetAttributes())
        {
            var type = attribute.AttributeClass;
            var withNavigation = type is { Name: "WithNavigation" } && type.ContainingType is not null;
            var kindType = withNavigation ? type!.ContainingType : type;
            if (kindType is not { TypeArguments.Length: >= 1 } || kindType.ContainingType is not { Name: "Relation" } container
                || container.ContainingNamespace.ToDisplayString() != "Pragmatic.Persistence.Entity" || kindType.TypeArguments[0] is not INamedTypeSymbol target)
                continue;
            string? Named(string key) => attribute.NamedArguments.FirstOrDefault(n => n.Key == key).Value.Value as string;
            var name = withNavigation && attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
            var required = attribute.NamedArguments.FirstOrDefault(n => n.Key == "Required").Value.Value is not false;
            var principal = attribute.NamedArguments.FirstOrDefault(n => n.Key == "IsPrincipal").Value.Value is true;
            var onDelete = attribute.NamedArguments.FirstOrDefault(n => n.Key == "OnDelete").Value is { Type: INamedTypeSymbol behavior, Value: { } value }
                ? behavior.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value))?.Name
                : null;
            yield return new Declared(kindType.Name, entity, target, name, Named("Inverse"), Named("ForeignKey"), required, principal, Named("JoinTable"), onDelete);
        }
    }
}
