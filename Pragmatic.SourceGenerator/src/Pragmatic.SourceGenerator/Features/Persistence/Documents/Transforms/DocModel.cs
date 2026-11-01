using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>The kinds of value a document field holds, each written and read one way.</summary>
internal enum DocKind
{
    String,
    Int32,
    Int64,

    /// <summary>A <c>ulong</c>, stored as an Int64 (BSON and JSON have no unsigned 64-bit number): a Pragmatic entity's RowVersion.</summary>
    UInt64,
    Double,
    Decimal,
    Boolean,
    Guid,
    DateTimeOffset,
    DateTime,
    Enum,
    Embedded,
    List,

    /// <summary>A <c>Pragmatic.Cryptography.ProtectedValue</c>: its packed bytes as Base64 text, as the audit trail's hashes are stored.</summary>
    Protected,
}

/// <summary>One field of a document: the property, its stored name, its kind; an embedded type's model, a list's element.</summary>
internal sealed class DocField
{
    public DocField(IPropertySymbol property, string name, DocKind kind, bool nullable, INamedTypeSymbol? embedded, DocKind elementKind, ITypeSymbol? element)
    {
        Property = property;
        Name = name;
        Kind = kind;
        Nullable = nullable;
        Embedded = embedded;
        ElementKind = elementKind;
        Element = element;
    }

    public IPropertySymbol Property { get; }

    /// <summary>The field's name in the document: <c>_id</c> for the key, <c>_v</c> for the version, the property's name otherwise.</summary>
    public string Name { get; }

    public DocKind Kind { get; }

    /// <summary>A Nullable&lt;T&gt; of a value kind, or a reference that may be null.</summary>
    public bool Nullable { get; }

    /// <summary>The [Embedded] type of an embedded field, or of a list's elements; the owned type of an <see cref="Owned" /> field.</summary>
    public INamedTypeSymbol? Embedded { get; }

    /// <summary>
    ///     A Pragmatic entity's owned type (<c>[Owned]</c>, <c>[ValueObject]</c>, an identity record) as a nested document: it is not this
    ///     engine's partial, so it is written and read through the codec the generator emits for it (<see cref="Codec" />).
    /// </summary>
    public bool Owned { get; init; }

    /// <summary>The generated codec of an <see cref="Owned" /> field's type, fully qualified; null for any other field.</summary>
    public string? Codec => Owned ? DocOwnedCodec.QualifiedName(Embedded!) : null;

    /// <summary>The kind of a list's elements.</summary>
    public DocKind ElementKind { get; }

    /// <summary>The type of a list's elements.</summary>
    public ITypeSymbol? Element { get; }

    /// <summary>The scalar type the field stores (the Nullable's underlying type for a nullable value), fully qualified.</summary>
    public string ScalarName => (Property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : Property.Type).ToDisplayString(DocModel.Qualified);
}

/// <summary>A [Document] or [Embedded] type as the generator reads it: its fields, its key and version, the collection it is stored in.</summary>
internal sealed class DocModel
{
    public const string DocumentAttribute = "Pragmatic.Persistence.Documents.Attributes.DocumentAttribute";
    public const string EmbeddedAttribute = "Pragmatic.Persistence.Documents.Attributes.EmbeddedAttribute";

    /// <summary>Pragmatic's <c>[Entity]</c>: a document too, its shape Pragmatic's (<c>PragmaticShape</c>), the relations kept as their keys.</summary>
    public const string EntityAttribute = "Pragmatic.Persistence.Entity.EntityAttribute";

    /// <summary>Fully qualified, without nullable annotations (a type's name in generated code).</summary>
    public static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private DocModel(INamedTypeSymbol type, string? collection, List<DocField> fields, string? partitionKey)
    {
        Type = type;
        Collection = collection;
        Fields = fields;
        Key = fields.FirstOrDefault(f => f.Name == "_id");
        Version = fields.FirstOrDefault(f => f.Name == "_v");
        PartitionKey = (partitionKey is null ? null : fields.FirstOrDefault(f => f.Property.Name == partitionKey)) ?? Key;
    }

    /// <summary>The field the documents are partitioned by (the [Document]'s PartitionKey; the key when it names none).</summary>
    public DocField? PartitionKey { get; }

    /// <summary>The field's name in Cosmos DB's JSON: <c>id</c> for the key.</summary>
    public static string JsonName(DocField field) => field.Name == "_id" ? "id" : field.Name;

    public INamedTypeSymbol Type { get; }

    public string FullName => Type.ToDisplayString(Qualified);

    /// <summary>The collection of a [Document]; null for an [Embedded] type.</summary>
    public string? Collection { get; }

    public List<DocField> Fields { get; }

    public DocField? Key { get; }

    public DocField? Version { get; }

    public bool IsEmbedded => Collection is null;

    /// <summary>A Pragmatic <c>[Entity]</c>: its reads carry the caller's filter families.</summary>
    public bool IsPragmaticEntity => IsPragmatic(Type);

    /// <summary>
    ///     The field of <paramref name="property" />; or, for a get-only property that only returns a stored one (a Pragmatic entity's
    ///     <c>Id =&gt; PersistenceId</c>), that one's field, as the Sql engine reads it as that column (<c>EntityModel.AliasTarget</c>).
    /// </summary>
    public DocField? Field(string property)
        => Fields.FirstOrDefault(f => f.Property.Name == property)
           ?? (AliasOf(property) is { } target ? Fields.FirstOrDefault(f => f.Property.Name == target) : null);

    private string? AliasOf(string property)
    {
        for (var type = Type; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(property).OfType<IPropertySymbol>().FirstOrDefault() is { } symbol)
                return EntityModel.AliasTarget(symbol);
        }

        return null;
    }

    public static bool IsDocument(ITypeSymbol? type) => type is INamedTypeSymbol named && named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is DocumentAttribute or EntityAttribute);

    /// <summary>True for a Pragmatic <c>[Entity]</c>.</summary>
    public static bool IsPragmatic(ITypeSymbol? type) => type is INamedTypeSymbol named && named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == EntityAttribute);

    /// <summary>A Pragmatic entity, or a collection of them: a navigation, which a document leaves out (its key is the relation).</summary>
    private static bool IsNavigation(ITypeSymbol type)
        => IsPragmatic(type) || (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic && type.SpecialType != SpecialType.System_String && IsPragmatic(generic.TypeArguments[0]));

    public static bool IsEmbeddedType(ITypeSymbol? type) => type is INamedTypeSymbol named && named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == EmbeddedAttribute);

    /// <summary>
    ///     An owned type by the rule both other branches use (<c>EntityModel.IsOwned</c>): a class the developer did not make this engine's
    ///     <c>[Embedded]</c> partial. A struct is not one: its codec's null is the reference's.
    /// </summary>
    public static bool IsOwnedType(ITypeSymbol? type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Class } named && !IsEmbeddedType(named) && EntityModel.IsOwned(named);

    /// <summary>
    ///     The model of a [Document], [Embedded] or owned type, its unsupported properties reported; null when it is none of them, or has no key
    ///     (a document). An owned type is a field only of a Pragmatic entity, or of another owned type.
    /// </summary>
    public static DocModel? Read(INamedTypeSymbol type, List<Diagnostic> diagnostics)
    {
        var document = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DocumentAttribute);
        var pragmatic = IsPragmatic(type);
        var owned = document is null && !pragmatic && IsOwnedType(type);
        if (document is null && !pragmatic && !IsEmbeddedType(type) && !owned)
            return null;
        // A Pragmatic entity's collection is its table's name (Pragmatic's plural), unless a [Document] next to it names one.
        var collection = document is null
            ? pragmatic ? PragmaticNames.Pluralize(type.Name) : null
            : document.ConstructorArguments.FirstOrDefault().Value as string ?? (pragmatic ? PragmaticNames.Pluralize(type.Name) : type.Name);
        var where = type.Locations.FirstOrDefault() ?? Location.None;
        var fields = new List<DocField>();
        var ok = true;
        var versioned = pragmatic && HasPragmatic(type, "ConcurrencyAwareAttribute");
        foreach (var property in Properties(type, pragmatic))
        {
            if (owned && property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                continue; // as the Sql engine leaves it out of the owner's columns
            // Pragmatic's shape: the key PersistenceId, the row version of a [ConcurrencyAware] entity (owner's decision of 2026-10-22: _v).
            var isKey = pragmatic ? property.Name == "PersistenceId" : property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Documents.Attributes.KeyAttribute");
            var isVersion = pragmatic ? versioned && property.Name == "RowVersion" : property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Documents.Attributes.VersionAttribute");
            var field = FieldOf(property, isKey ? "_id" : isVersion ? "_v" : property.Name, pragmatic || owned, out var reason);
            if (field is null)
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.UnsupportedMember, property.Locations.FirstOrDefault() ?? where, type.Name, property.Name, property.Type.ToDisplayString(), reason));
                ok = false;
                continue;
            }

            if (isVersion && field.Kind is not (DocKind.Int32 or DocKind.Int64 or DocKind.UInt64))
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.UnsupportedMember, property.Locations.FirstOrDefault() ?? where, type.Name, property.Name, property.Type.ToDisplayString(), "a [Version] is an int or a long"));
                ok = false;
                continue;
            }

            fields.Add(field);
        }

        if ((document is not null || pragmatic) && !fields.Any(f => f.Name == "_id"))
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.UnsupportedMember, where, type.Name, "(key)", "-", "a [Document] has one [Key] property, stored as _id"));
            return null;
        }

        // A Pragmatic tenant's entity is partitioned by its tenant (owner's decision of 2026-10-22), unless a [Document] next to it says otherwise.
        var partitionKey = document?.NamedArguments.FirstOrDefault(a => a.Key == "PartitionKey").Value.Value as string
                           ?? (pragmatic && type.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity") ? "TenantId" : null);
        if (partitionKey is not null && !fields.Any(f => f.Property.Name == partitionKey && f.Kind is not (DocKind.Embedded or DocKind.List)))
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.UnsupportedMember, where, type.Name, partitionKey, "-", "the PartitionKey names a scalar property of the document"));
            return null;
        }

        return ok ? new DocModel(type, collection, fields, partitionKey) : null;
    }

    /// <summary>
    ///     The public instance properties with a getter and a setter, base types' first. A Pragmatic entity's setter may be its shape's private or
    ///     internal one (the readers are the type's own partial); its navigations and its <c>[NotMapped]</c> members are not fields.
    /// </summary>
    private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol type, bool pragmatic)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            chain.Insert(0, t);
        foreach (var t in chain)
        {
            foreach (var property in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null || property.SetMethod is null)
                    continue;
                if (pragmatic ? IsNavigation(property.Type) || property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute") : property.SetMethod.DeclaredAccessibility != Accessibility.Public)
                    continue;
                yield return property;
            }
        }
    }

    /// <summary>A Pragmatic trait's attribute on the type or a base type, by name.</summary>
    private static bool HasPragmatic(INamedTypeSymbol type, string attribute)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.GetAttributes().Any(a => a.AttributeClass?.Name == attribute && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Pragmatic.Persistence.Entity"))
                return true;
        }

        return false;
    }

    // allowOwned: a Pragmatic entity's field, or an owned type's. An owned type there is a nested document, through its codec.
    private static DocField? FieldOf(IPropertySymbol property, string name, bool allowOwned, out string reason)
    {
        reason = "";
        var type = property.Type;
        var nullable = type.NullableAnnotation == NullableAnnotation.Annotated || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var scalar = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapped ? wrapped.TypeArguments[0] : type;
        if (ScalarKind(scalar) is { } kind)
            return new DocField(property, name, kind, nullable, null, kind, null);
        if (IsEmbeddedType(scalar))
            return new DocField(property, name, DocKind.Embedded, nullable, (INamedTypeSymbol)scalar, DocKind.Embedded, scalar);
        if (allowOwned && IsOwnedType(scalar))
            return new DocField(property, name, DocKind.Embedded, nullable, (INamedTypeSymbol)scalar.WithNullableAnnotation(NullableAnnotation.NotAnnotated), DocKind.Embedded, scalar) { Owned = true };
        if (scalar is INamedTypeSymbol { Name: "List", TypeArguments.Length: 1 } list && list.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
        {
            var element = list.TypeArguments[0];
            if (IsEmbeddedType(element))
                return new DocField(property, name, DocKind.List, nullable, (INamedTypeSymbol)element, DocKind.Embedded, element);
            if (ScalarKind(element) is { } elementKind and not (DocKind.Enum or DocKind.Protected))
                return new DocField(property, name, DocKind.List, nullable, null, elementKind, element);
            reason = "a list holds [Embedded] documents or values of one kind (string, int, long, double, decimal, bool, Guid, DateTimeOffset)";
            return null;
        }

        reason = IsDocument(scalar)
            ? "a document does not hold another document: refer to it by its key, or make the type [Embedded]"
            : "the kinds are string, int, long, double, decimal, bool, Guid, DateTimeOffset, DateTime, an enum, a ProtectedValue, an [Embedded] type, and a List of these";
        return null;
    }

    /// <summary>
    ///     The result of a grouped Select as a model: its scalar members as the fields of the <c>$group</c> stage's documents (the key as
    ///     <c>_id</c>, an aggregate under its member's name). What a Where or an ordering after that Select translates against.
    /// </summary>
    public static DocModel Group(INamedTypeSymbol result, IReadOnlyDictionary<string, string> fieldOf)
    {
        var fields = new List<DocField>();
        foreach (var property in result.GetMembers().OfType<IPropertySymbol>())
        {
            if (fieldOf.TryGetValue(property.Name, out var name) && FieldOf(property, name, allowOwned: false, out _) is { } field)
                fields.Add(field);
        }

        return new DocModel(result, "", fields, null);
    }

    public static DocKind? ScalarKind(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
            return DocKind.Enum;
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return DocKind.String;
            case SpecialType.System_Int32: return DocKind.Int32;
            case SpecialType.System_Int64: return DocKind.Int64;
            case SpecialType.System_UInt64: return DocKind.UInt64;
            case SpecialType.System_Double: return DocKind.Double;
            case SpecialType.System_Decimal: return DocKind.Decimal;
            case SpecialType.System_Boolean: return DocKind.Boolean;
            case SpecialType.System_DateTime: return DocKind.DateTime;
        }

        var name = type.ToDisplayString();
        return name == "System.Guid" ? DocKind.Guid
            : name == "System.DateTimeOffset" ? DocKind.DateTimeOffset
            : name == Pragmatic.SourceGenerator.Core.ProtectedValueType.FullName ? DocKind.Protected
            : null;
    }
}
