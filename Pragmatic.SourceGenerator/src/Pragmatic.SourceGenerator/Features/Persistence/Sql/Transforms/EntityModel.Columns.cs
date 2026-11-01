using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>One property as a column: its converter, its reader, its name and its marks.</summary>
internal sealed partial class EntityModel
{
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

        // A ProtectedValue: its packed bytes in a binary column, as EF Core's ProtectedValueConverter stores them, through the converter of
        // Pragmatic.Cryptography.Engines (Read refuses the entity when the compilation does not have it).
        if (converter is null && IsProtectedValue(property.Type))
        {
            converter = "global::" + ProtectedValueType.SqlConverterFullName;
            store = ((INamedTypeSymbol)Unwrap(property.Type)).GetMembers("Packed").OfType<IPropertySymbol>().First().Type;
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

    /// <summary>
    ///     False, with a diagnostic naming the package, when a column stores a <c>ProtectedValue</c> and the compilation lacks the converter it is
    ///     read and bound through: said here, on the property, rather than as a missing type in a generated file nobody wrote (as PRAG0652 does
    ///     for EF Core).
    /// </summary>
    private static bool CanStoreProtectedValues(INamedTypeSymbol type, List<ColumnModel> columns, Compilation compilation, List<Diagnostic> diagnostics)
    {
        var converter = "global::" + ProtectedValueType.SqlConverterFullName;
        if (compilation.GetTypeByMetadataName(ProtectedValueType.SqlConverterFullName) is not null || columns.All(c => c.Converter != converter))
            return true;
        foreach (var column in columns.Where(c => c.Converter == converter))
        {
            var property = type.GetMembers(column.Property.Split('.')[0]).FirstOrDefault();
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedPropertyType, property?.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault(), column.Property, type.Name,
                $"{ProtectedValueType.FullName}, stored through {ProtectedValueType.SqlConverterFullName}: reference the Pragmatic.Cryptography.Engines package"));
        }

        return false;
    }

    /// <summary><c>Pragmatic.Cryptography.ProtectedValue</c>, or its nullable form: by its full name, not a type that merely ends the same way.</summary>
    internal static bool IsProtectedValue(ITypeSymbol type)
        => Unwrap(type).ToDisplayString() == ProtectedValueType.FullName;
}
