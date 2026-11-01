using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The entity's key and the names its columns answer to: the columns themselves, and the get-only properties that alias one.</summary>
internal sealed partial class EntityModel
{
    /// <summary>
    ///     The key columns: the properties marked <c>[Key]</c>, or <c>Id</c>, or <c>PersistenceId</c> (Pragmatic's <c>[Entity]</c>: the key its
    ///     generator declares, <c>Id</c> being a computed alias of it).
    /// </summary>
    public List<ColumnModel> Keys
    {
        get
        {
            var marked = Columns.Where(c => c.IsKey).ToList();
            if (marked.Count > 0)
                return marked;
            var id = Column("Id") ?? Column("PersistenceId");
            return id is null ? [] : [id];
        }
    }

    public bool HasCompositeKey => Keys.Count > 1;

    /// <summary>The key column of a single-column key (joins, includes, child plans): the first key column.</summary>
    public string KeyColumn => Keys.FirstOrDefault()?.Column ?? "Id";

    /// <summary>The key property of a single-column key.</summary>
    public string KeyProperty => Keys.FirstOrDefault()?.Property ?? "Id";

    public string KeyList => string.Join(", ", Keys.Select(k => k.Property));

    public ColumnModel? Column(string property)
        => Columns.FirstOrDefault(c => c.Property == property) ?? (Aliases.TryGetValue(property, out var target) ? Columns.FirstOrDefault(c => c.Property == target) : null);

    /// <summary>Get-only properties that only return a mapped one (Pragmatic's <c>Id =&gt; PersistenceId</c>): read as that column.</summary>
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The get-only properties whose body is the name of a mapped column (<c>public Guid Id =&gt; PersistenceId;</c>).</summary>
    private static Dictionary<string, string> AliasesOf(INamedTypeSymbol type, List<ColumnModel> columns)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (AliasTarget(property) is { } target && columns.Any(c => c.Owner is null && c.Property == target))
                aliases[property.Name] = target;
        }

        return aliases;
    }

    /// <summary>
    ///     The property a public, get-only, instance property only returns, by name (<c>public Guid Id =&gt; PersistenceId;</c>); null for any other
    ///     body. Both engines read such a property as the one it returns, when that one is stored: the documents engine through
    ///     <c>DocModel.Field</c>.
    /// </summary>
    internal static string? AliasTarget(IPropertySymbol property)
    {
        if (property.IsStatic || property.SetMethod is not null || property.DeclaredAccessibility != Accessibility.Public)
            return null;
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody.Expression: IdentifierNameSyntax target })
                return target.Identifier.ValueText;
        }

        return null;
    }
}
