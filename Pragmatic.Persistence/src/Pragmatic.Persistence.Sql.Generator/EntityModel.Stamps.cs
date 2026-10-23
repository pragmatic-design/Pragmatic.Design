using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The columns the writer fills itself: <c>[SoftDelete]</c> (IsDeleted, DeletedAt, DeletedBy) and <c>[Auditable]</c> (CreatedAt/By,
///     UpdatedAt/By), read by name on the type or a base type, as the repository's attributes are.
/// </summary>
internal sealed partial class EntityModel
{
    /// <summary>A [SoftDelete] entity's flag column; null otherwise.</summary>
    public string? SoftDeleteColumn => _isDeleted?.Column;

    /// <summary>True when a graph delete marks the [SoftDelete] rows of the collections too (<c>[SoftDelete(Cascade = true)]</c>).</summary>
    public bool SoftDeleteCascade { get; private set; }

    /// <summary>The live rows of a [SoftDelete] entity, in the fragment form; null otherwise. <c>IncludeDeleted()</c> and <c>WithoutFilters()</c> read past it.</summary>
    public string? SoftDeletePredicate => MarkedSoftDelete(_isDeleted is null ? null : $"t.\"{_isDeleted.Column}\" = {{bt:0}}");

    /// <summary>
    ///     What every read of this entity's rows under another carries (an include, a subquery, a join): the derived type's rows, the live ones,
    ///     and a Pragmatic entity's families (tenant, owner, scopes).
    /// </summary>
    public string? RowPredicate
    {
        get
        {
            var parts = new[] { FixedPredicate, SoftDeletePredicate, FamilyPredicate }.Where(p => p is not null).ToList();
            return parts.Count == 0 ? null : string.Join(" AND ", parts);
        }
    }

    private ColumnModel? _isDeleted;
    private ColumnModel? _deletedAt;
    private ColumnModel? _deletedBy;
    private ColumnModel? _createdAt;
    private ColumnModel? _createdBy;
    private ColumnModel? _updatedAt;
    private ColumnModel? _updatedBy;

    /// <summary>Reads [SoftDelete] and [Auditable]; false with a diagnostic when a stamped property has a type the writer cannot fill.</summary>
    private bool ReadStamps(List<Diagnostic> diagnostics)
    {
        var softDelete = MarkOf("SoftDeleteAttribute");
        var auditable = MarkOf("AuditableAttribute");
        if (softDelete is not null)
        {
            _isDeleted = Stamped("IsDeleted", required: true, diagnostics, ColumnKind.Flag);
            if (_isDeleted is null)
                return false;
            if (!Stamp("DeletedAt", ColumnKind.Optional | ColumnKind.Clock, diagnostics, out _deletedAt) || !Stamp("DeletedBy", ColumnKind.Optional | ColumnKind.User, diagnostics, out _deletedBy))
                return false;
            SoftDeleteCascade = softDelete.NamedArguments.Any(n => n.Key == "Cascade" && n.Value.Value is true);
        }

        if (auditable is not null)
        {
            if (!Stamp("CreatedAt", ColumnKind.Clock, diagnostics, out _createdAt) || !Stamp("CreatedBy", ColumnKind.User, diagnostics, out _createdBy)
                || !Stamp("UpdatedAt", ColumnKind.Clock, diagnostics, out _updatedAt) || !Stamp("UpdatedBy", ColumnKind.User, diagnostics, out _updatedBy))
                return false;
        }

        return true;
    }

    /// <summary>The columns the root writer stamps; null when the entity stamps nothing.</summary>
    public StampsModel? Stamps()
    {
        if (_isDeleted is null && _createdAt is null && _createdBy is null && _updatedAt is null && _updatedBy is null)
            return null;
        static StampColumnModel? Of(ColumnModel? column)
            => column is null ? null : new StampColumnModel(column.Property, column.Column, Unwrap(column.Type).SpecialType == SpecialType.System_DateTime);
        return new StampsModel(FullName, Of(_createdAt), Of(_createdBy), Of(_updatedAt), Of(_updatedBy), Of(_isDeleted), Of(_deletedAt), Of(_deletedBy));
    }

    [System.Flags]
    private enum ColumnKind
    {
        Flag = 1,
        Clock = 2,
        User = 4,
        Optional = 8,
    }

    private AttributeData? MarkOf(string name)
    {
        for (var current = Type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == name) is { } mark)
                return mark; // on a hierarchy's root, the derived types' rows are stamped and filtered too
        }

        return null;
    }

    private bool Stamp(string property, ColumnKind kind, List<Diagnostic> diagnostics, out ColumnModel? column)
    {
        column = Stamped(property, required: false, diagnostics, kind);
        return column is not null || Column(property) is null; // absent is fine; present with the wrong type is not
    }

    /// <summary>The stamped column, checked: a bool flag, a DateTimeOffset or DateTime clock, a string user (which may be unknown: string?); null when absent or wrong.</summary>
    private ColumnModel? Stamped(string property, bool required, List<Diagnostic> diagnostics, ColumnKind kind)
    {
        var column = Column(property);
        var location = Type.Locations.FirstOrDefault();
        if (column is null || column.Owner is not null || column.Converter is not null)
        {
            if (required || column is not null)
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, location, property, Type.Name, required ? "missing: a [SoftDelete] entity declares bool IsDeleted" : "a stamped column of the entity itself, stored as it is (no owner, no converter)"));
            return null;
        }

        var type = Unwrap(column.Type);
        var optional = IsNullable(column.Type);
        var expected = (kind & ColumnKind.Flag) != 0 ? type.SpecialType == SpecialType.System_Boolean && !optional
            : (kind & ColumnKind.Clock) != 0 ? (type.SpecialType == SpecialType.System_DateTime || type.ToDisplayString() == "System.DateTimeOffset") && (optional || (kind & ColumnKind.Optional) == 0)
            : type.SpecialType == SpecialType.System_String && column.Type.NullableAnnotation != NullableAnnotation.NotAnnotated; // the user may be unknown
        if (expected)
            return column;
        var wanted = (kind & ColumnKind.Flag) != 0 ? "bool" : (kind & ColumnKind.Clock) != 0 ? (kind & ColumnKind.Optional) != 0 ? "DateTimeOffset? or DateTime?" : "DateTimeOffset or DateTime" : "string?";
        diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, location, property, Type.Name, $"{column.Type.ToDisplayString()} stamped by the writer: it is {wanted}"));
        return null;
    }
}
