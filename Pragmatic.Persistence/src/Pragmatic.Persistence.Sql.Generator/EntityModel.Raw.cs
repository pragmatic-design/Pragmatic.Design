using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

internal sealed partial class EntityModel
{
    /// <summary>
    ///     A DTO as the row shape of a hand-written statement (<c>session.Sql&lt;TDto&gt;</c>): every settable property is a column of the
    ///     same name, read in order; no table, no navigations (the statement already joined what it needed). Null with a diagnostic
    ///     when a property's type is not read.
    /// </summary>
    public static EntityModel? ForRaw(INamedTypeSymbol type, List<Diagnostic> diagnostics)
    {
        var columns = new List<ColumnModel>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.SetMethod is not null && p.DeclaredAccessibility == Accessibility.Public))
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue;
            var column = TryColumn(property, type, diagnostics, owner: null, optionalOwner: false);
            if (column is null)
                return null;
            columns.Add(column);
        }

        return new EntityModel(type, "", columns, [], [], [], null, false);
    }
}
