using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The members Pragmatic's generator derives from <c>[Relation.*]</c> (<c>RelationGraphBuilder</c>, <c>EntityRelationsTemplate</c>): foreign
///     keys, reference navigations, collections, the foreign keys' setters, on the declaring entity and the inverse on the other. Rules:
///     a reference is named after its target, a collection after its target pluralized; a foreign key is the navigation's name and Id (a Guid,
///     nullable when a ManyToOne says Required = false); OneToMany puts the inverse on the child; ManyToOne puts nothing on its target;
///     ManyToMany puts the other side's collection unless that side declares it; a member the source declares, or a trait's name, is skipped.
///     A many-to-many collection carries the Sql engine's <c>[ManyToMany]</c> with Pragmatic's join table and columns (<c>JoinTableNaming</c>,
///     <c>JoinColumnNaming</c>): the same member, read by this engine.
///     ASSUMPTION: the entities share a boundary (across boundaries Pragmatic leaves some navigations out, which is not reproduced here).
/// </summary>
internal sealed class PragmaticRelations
{
    private static readonly HashSet<string> Reserved = ["PersistenceId", "Id", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy", "OwnerId"];

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

    // Per entity, in Pragmatic's order: foreign keys (0), references (1), collections (2), setters (3); the first member of a name wins.
    private readonly Dictionary<INamedTypeSymbol, List<(int Order, string Name, string Text)>> _members = new(SymbolEqualityComparer.Default);

    // The Sql engine's [ManyToMany] on a many-to-many collection; the documents engine keeps no join table (the relation is not stored there yet).
    private bool _sqlMapping = true;

    public static PragmaticRelations Read(List<INamedTypeSymbol> entities, bool sqlMapping = true)
    {
        var relations = new PragmaticRelations { _sqlMapping = sqlMapping };
        var declared = entities.SelectMany(Declarations).ToList();
        foreach (var relation in declared)
            relations.Add(relation, declared);
        return relations;
    }

    /// <summary>The members of <paramref name="entity" /> not already declared, in order, appended to <paramref name="body" />.</summary>
    public void AppendMembers(INamedTypeSymbol entity, HashSet<string> declared, StringBuilder body)
    {
        if (!_members.TryGetValue(entity, out var members))
            return;
        var source = new HashSet<string>(entity.GetMembers().Select(m => m.Name));
        foreach (var member in members.OrderBy(m => m.Order))
        {
            if (member.Order == 3 && source.Contains(member.Name.Substring("Set".Length)))
                continue; // a foreign key the source declares takes its setter with it (RelationGraphBuilder: per member)
            if (!Reserved.Contains(member.Name) && declared.Add(member.Name))
                body.AppendLine(member.Text);
        }
    }

    private void Add(Declared relation, List<Declared> all)
    {
        var owner = relation.Owner;
        var target = relation.Target;
        switch (relation.Kind)
        {
            case "ManyToOne":
                Reference(owner, target, relation.Name ?? target.Name, relation.ForeignKey, relation.Required, withKey: true);
                break;
            case "OneToMany":
            {
                Member(owner, 2, relation.Name ?? PragmaticNames.Pluralize(target.Name), $"    public global::System.Collections.Generic.ICollection<{Full(target)}> {relation.Name ?? PragmaticNames.Pluralize(target.Name)} {{ get; set; }} = [];");
                var counterpart = Counterpart(all, target, owner, "ManyToOne", relation.Inverse);
                if (counterpart is null) // the child's side, inferred: named after the parent unless the parent says Inverse
                    Reference(target, owner, relation.Inverse ?? owner.Name, null, required: true, withKey: true);
                break;
            }

            case "OneToOne":
            {
                var name = relation.Name ?? target.Name;
                Reference(owner, target, name, relation.ForeignKey, required: true, withKey: !relation.IsPrincipal);
                // The principal's navigation back, when it does not declare one. INVESTIGATE: a principal declaring end leaves its dependent unshaped here.
                if (!relation.IsPrincipal && Counterpart(all, target, owner, "OneToOne", relation.Inverse) is null)
                    Reference(target, owner, relation.Inverse ?? owner.Name, null, required: true, withKey: false);
                break;
            }

            case "ManyToMany":
            {
                var name = relation.Name ?? PragmaticNames.Pluralize(target.Name);
                var table = relation.JoinTable ?? (string.CompareOrdinal(owner.Name, target.Name) <= 0 ? owner.Name + target.Name : target.Name + owner.Name);
                var (left, right) = owner.Name != target.Name ? (owner.Name + "Id", target.Name + "Id") : ((relation.Inverse ?? "Inverse") + "Id", name + "Id");
                Member(owner, 2, name, Collection(target, name, table, left, right));
                if (Counterpart(all, target, owner, "ManyToMany", null) is null)
                {
                    var inverse = relation.Inverse ?? PragmaticNames.Pluralize(owner.Name);
                    Member(target, 2, inverse, Collection(owner, inverse, table, right, left));
                }

                break;
            }
        }
    }

    /// <summary>A reference: its foreign key (a Guid, nullable when optional) and its setter when it has one, and the navigation.</summary>
    private void Reference(INamedTypeSymbol owner, INamedTypeSymbol target, string name, string? foreignKey, bool required, bool withKey)
    {
        if (withKey)
        {
            var fk = foreignKey ?? name + "Id";
            var type = required ? "global::System.Guid" : "global::System.Guid?";
            Member(owner, 0, fk, $"    public {type} {fk} {{ get; private set; }}");
            Member(owner, 3, "Set" + fk, $"    internal void Set{fk}({type} value) => {fk} = value;");
        }

        if (!PragmaticShape.Has(target, "LookupAttribute")) // a [Lookup] target gets a resolver, not a navigation (LookupNavigationTemplate)
            Member(owner, 1, name, required ? $"    public {Full(target)} {name} {{ get; set; }} = null!;" : $"    public {Full(target)}? {name} {{ get; set; }}");
    }

    private string Collection(INamedTypeSymbol element, string name, string table, string thisKey, string otherKey)
        => (_sqlMapping ? $"    [global::Pragmatic.Persistence.Sql.ManyToMany(\"{table}\", \"{thisKey}\", \"{otherKey}\")]\n" : "")
           + $"    public global::System.Collections.Generic.ICollection<{Full(element)}> {name} {{ get; set; }} = [];";

    private void Member(INamedTypeSymbol entity, int order, string name, string text)
    {
        if (!_members.TryGetValue(entity, out var members))
            _members[entity] = members = [];
        if (!members.Any(m => m.Name == name))
            members.Add((order, name, text));
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

    private static string Full(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
