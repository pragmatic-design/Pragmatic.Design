using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The permission that lets a caller past an entity's row-level filters, named as Pragmatic names it:
///     <c>{boundary}.{entity-kebab}.view-all</c>, or <c>{entity-kebab}.view-all</c> without a boundary (<c>PermissionNaming.ViewAllPermission</c>).
///     The boundary is Pragmatic's <c>BoundaryOwnershipReader.BoundaryOf</c>: the entity's <c>[BelongsTo&lt;T&gt;]</c>, else the
///     <c>[Boundary]</c> of its assembly that claims it with <c>[Owns&lt;T&gt;]</c>, else the assembly's only <c>[Boundary]</c>.
/// </summary>
internal static class PragmaticPermissions
{
    private const string BoundaryAttribute = "Pragmatic.Actions.Attributes.BoundaryAttribute";
    private const string OwnsAttributePrefix = "Pragmatic.Actions.Attributes.OwnsAttribute";

    public static string ViewAll(INamedTypeSymbol entity)
    {
        var boundary = BoundaryName(entity)?.ToLowerInvariant();
        var prefix = string.IsNullOrEmpty(boundary) ? Kebab(entity.Name) : $"{Kebab(boundary!)}.{Kebab(entity.Name)}";
        return prefix + ".view-all";
    }

    private static string? BoundaryName(INamedTypeSymbol entity) => BoundaryOf(entity) is { } boundary ? StripSuffix(boundary.Name) : null;

    /// <summary>The entity's boundary type, by Pragmatic's three rules; null when none answers (no boundary, or several and none claims it).</summary>
    public static ITypeSymbol? BoundaryOf(INamedTypeSymbol entity)
    {
        var belongsTo = entity.GetAttributes().Select(a => a.AttributeClass)
            .FirstOrDefault(c => c is { IsGenericType: true, OriginalDefinition.Name: "BelongsToAttribute", TypeArguments.Length: 1 }
                                 && c.OriginalDefinition.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity");
        if (belongsTo is not null)
            return belongsTo.TypeArguments[0];
        if (entity.ContainingAssembly is not { } assembly)
            return null;

        var boundaries = PragmaticNames.AllTypes(assembly.GlobalNamespace)
            .Where(t => t.GetAttributes().Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString() == BoundaryAttribute)).ToList();
        var owner = boundaries.FirstOrDefault(b => b.GetAttributes().Any(a => a.AttributeClass is { } c
            && c.OriginalDefinition.ToDisplayString().StartsWith(OwnsAttributePrefix, System.StringComparison.Ordinal)
            && c.TypeArguments.Length > 0 && SymbolEqualityComparer.Default.Equals(c.TypeArguments[0], entity)));
        if (owner is not null)
            return owner;
        return boundaries.Count == 1 ? boundaries[0] : null;
    }

    private static string StripSuffix(string name) => name.EndsWith("Boundary", System.StringComparison.Ordinal) ? name.Substring(0, name.Length - "Boundary".Length) : name;

    /// <summary>PascalCase to kebab-case, Pragmatic's rule: a dash before every upper-case letter but the first.</summary>
    private static string Kebab(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
