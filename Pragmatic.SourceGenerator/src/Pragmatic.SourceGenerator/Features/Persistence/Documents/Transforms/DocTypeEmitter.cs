using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The model of a [Document] or [Embedded] partial class's generated half (<see cref="DocumentTypeTemplate" />), and the BSON
///     expressions that read and write a field, which the planner's code uses too.
/// </summary>
internal static class DocTypeEmitter
{
    private const string Runtime = "global::Pragmatic.Persistence.Documents";
    private const string Bson = "global::Pragmatic.Persistence.Documents.Mongo.Bson";

    /// <summary>The type's partial, or null (reported) when the type cannot take one: not partial, nested, generic.</summary>
    public static DocumentTypeModel? Model(DocModel model, string hint, List<Diagnostic> diagnostics, List<string>? families = null)
    {
        var type = model.Type;
        var where = type.Locations.FirstOrDefault() ?? Location.None;
        var partial = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().Any(d => d.Modifiers.Any(m => m.ValueText == "partial"));
        if (!partial || type.ContainingType is not null || type.IsGenericType || type.TypeKind != TypeKind.Class)
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.NotPartial, where, type.Name + (type.ContainingType is not null ? " (a top-level class: nested types are not supported)" : type.IsGenericType ? " (a non-generic class)" : "")));
            return null;
        }

        var name = model.FullName;
        var contract = model.IsEmbedded ? $"{Runtime}.IEmbeddedDocument<{name}>" : $"{Runtime}.IDocumentEntity<{name}>";
        var accessors = new List<string>();
        var fields = model.Fields.Select(f => new DocumentFieldModel(
            f.Property.Name,
            Literal(f.Name),
            WriteField(f, "e." + f.Property.Name),
            ReadField(f),
            Literal(DocModel.JsonName(f)),
            DocJsonEmitter.WriteField(f, "e." + f.Property.Name),
            f.Name == "_id" ? DocJsonEmitter.ParseId(f) : DocJsonEmitter.ReadField(f),
            f.Name == "_id",
            Setter(type, f.Property, accessors))).ToEquatableArray();
        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        if (model.IsEmbedded)
            return new DocumentTypeModel(hint, ns, type.Name, name, contract, IsEmbedded: true, null, fields, null, false, "0", "", null, null, null, (families ?? []).ToEquatableArray(), accessors.ToEquatableArray());

        var key = model.Key!;
        var version = model.Version;
        var partition = model.PartitionKey!;
        var versionSet = version is null ? "" : fields.First(f => f.PropertyName == version.Property.Name).Assign($"unchecked(({version.ScalarName})version)") + " ";
        return new DocumentTypeModel(
            hint, ns, type.Name, name, contract, IsEmbedded: false,
            Literal(model.Collection!),
            fields,
            WriteField(key, "e." + key.Property.Name, "name"),
            version is not null,
            version is null ? "0" : "unchecked((long)e." + version.Property.Name + ")",
            versionSet,
            DocJsonEmitter.IdText(key, "e." + key.Property.Name),
            Literal("/" + DocModel.JsonName(partition)),
            DocJsonEmitter.PartitionKeyWrite(key, partition),
            (families ?? []).ToEquatableArray(),
            accessors.ToEquatableArray(),
            model.Inheritance is { } inheritance
                ? new DocumentInheritanceModel(Literal(inheritance.Discriminator), Literal(DocInheritanceTree.NameOf(type)), type.IsAbstract,
                    inheritance.Below.Select(b => new DocumentSubtypeModel(b.ToDisplayString(DocModel.Qualified), Literal(DocInheritanceTree.NameOf(b)))).ToEquatableArray())
                : null);
    }

    /// <summary>
    ///     The accessor a read sets <paramref name="property" /> through, its declaration added to <paramref name="accessors" />, when the partial of
    ///     <paramref name="type" /> cannot assign it: a base type's private setter (#539), as the Sql engine's <c>EntityMaps</c> sets one. Null when
    ///     the partial assigns it.
    /// </summary>
    private static string? Setter(INamedTypeSymbol type, IPropertySymbol property, List<string> accessors)
    {
        if (SymbolEqualityComparer.Default.Equals(property.ContainingType, type) || property.SetMethod is { DeclaredAccessibility: not Accessibility.Private })
            return null;
        var setter = "__Set_" + property.Name;
        accessors.Add($"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_{property.Name}\")]");
        accessors.Add($"private static extern void {setter}({property.ContainingType.ToDisplayString(DocModel.Qualified)} e, {property.Type.ToDisplayString(DocModel.Qualified)} value);");
        return setter;
    }

    /// <summary>The statement that writes one field's value under its name (or under <paramref name="nameExpression" />, a C# expression).</summary>
    public static string WriteField(DocField field, string value, string? nameExpression = null)
    {
        var name = nameExpression ?? Literal(field.Name);
        if (field.Owned)
            return $"{field.Codec}.Write(w, {name}, {value})";
        switch (field.Kind)
        {
            case DocKind.Embedded:
                return $"{Runtime}.Conversion.DocMapping.WriteEmbedded<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(w, {name}, {value})";
            case DocKind.List when field.ElementKind == DocKind.Embedded:
                return $"{Runtime}.Conversion.DocMapping.WriteEmbeddedList<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(w, {name}, {value})";
            case DocKind.List:
                return $"{Runtime}.Conversion.DocMapping.WriteList<{field.Element!.ToDisplayString(DocModel.Qualified)}>(w, {name}, {value}, {Runtime}.Conversion.DocValues.Write)";
            case DocKind.Enum:
                return field.Nullable
                    ? $"{Runtime}.Conversion.DocValues.Write(w, {name}, {value} is {{ }} v ? (int?)(int)v : null)"
                    : $"w.Write({name}, (int){value})";
            case DocKind.DateTime:
                return field.Nullable ? $"{Runtime}.Conversion.DocMapping.Write(w, {name}, {value})" : $"{Runtime}.Conversion.DocValues.Write(w, {name}, {value})";
            case DocKind.UInt64:
                return $"{Runtime}.Conversion.DocValues.Write(w, {name}, unchecked((long){value}))";
            case DocKind.Protected:
                return $"{Runtime}.Conversion.DocValues.Write(w, {name}, {ProtectedText(field, value)})";
            case DocKind.Localized:
                return $"{Runtime}.Conversion.DocValues.Write(w, {name}, {LocalizedText(field, value)})";
            default:
                return $"{Runtime}.Conversion.DocValues.Write(w, {name}, {value})";
        }
    }

    /// <summary>The expression that reads one field's value from a <c>BsonReader r</c> positioned on it.</summary>
    public static string ReadField(DocField field)
    {
        if (field.Owned)
            return $"{field.Codec}.Read(r.ReadDocument())";
        switch (field.Kind)
        {
            case DocKind.Embedded:
                return $"{Runtime}.Conversion.DocMapping.ReadEmbedded<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(r.ReadDocument())";
            case DocKind.List when field.ElementKind == DocKind.Embedded:
                return $"{Runtime}.Conversion.DocMapping.ReadEmbeddedList<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(r.ReadDocument())";
            case DocKind.List:
                return $"{Runtime}.Conversion.DocMapping.ReadList<{field.Element!.ToDisplayString(DocModel.Qualified)}>(r.ReadDocument(), static (ref {Bson}.BsonReader x) => {ReadScalar(field.ElementKind, "x", field.Element!.ToDisplayString(DocModel.Qualified))})";
            default:
                return ReadScalar(field.Kind, "r", field.ScalarName);
        }
    }

    private static string ReadScalar(DocKind kind, string reader, string typeName) => kind switch
    {
        DocKind.String => $"{reader}.ReadString()",
        DocKind.Int32 => $"{Runtime}.Conversion.DocMapping.ReadInt32(ref {reader})",
        DocKind.Int64 => $"{Runtime}.Conversion.DocMapping.ReadInt64(ref {reader})",
        DocKind.UInt64 => $"unchecked((ulong){Runtime}.Conversion.DocMapping.ReadInt64(ref {reader}))",
        DocKind.Double => $"{Runtime}.Conversion.DocMapping.ReadDouble(ref {reader})",
        DocKind.Decimal => $"{Runtime}.Conversion.DocMapping.ReadDecimal(ref {reader})",
        DocKind.Boolean => $"{reader}.ReadBoolean()",
        DocKind.Guid => $"{reader}.ReadGuid()",
        DocKind.DateTimeOffset => $"{reader}.ReadDateTime()",
        DocKind.DateTime => $"{Runtime}.Conversion.DocMapping.ReadDateTime(ref {reader})",
        DocKind.DateOnly => $"{Runtime}.Conversion.DocMapping.ReadDateOnly(ref {reader})",
        DocKind.TimeOnly => $"{Runtime}.Conversion.DocMapping.ReadTimeOnly(ref {reader})",
        DocKind.TimeSpan => $"{Runtime}.Conversion.DocMapping.ReadTimeSpan(ref {reader})",
        DocKind.Enum => $"({typeName}){Runtime}.Conversion.DocMapping.ReadInt32(ref {reader})",
        DocKind.Protected => ProtectedFromText($"{reader}.ReadString()"),
        DocKind.Localized => LocalizedFromText($"{reader}.ReadString()"),
        _ => $"default! /* {kind} */",
    };

    /// <summary>A <c>LocalizedString</c> as the JSON text the document holds, EF Core's converter's text; null for a null nullable one.</summary>
    internal static string LocalizedText(DocField field, string value)
        => field.Nullable ? $"{value}?.ToJson()" : $"{value}.ToJson()";

    /// <summary>The <c>LocalizedString</c> whose stored JSON is <paramref name="text" />.</summary>
    internal static string LocalizedFromText(string text)
        => $"global::{Pragmatic.SourceGenerator.Core.LocalizedStringType.FullName}.FromJson({text})";

    /// <summary>A <c>ProtectedValue</c>'s packed bytes as the Base64 text the document holds; null for a null nullable one.</summary>
    internal static string ProtectedText(DocField field, string value)
        => field.Nullable
            ? $"({value} is {{ }} protectedValue ? global::System.Convert.ToBase64String(protectedValue.Packed ?? global::System.Array.Empty<byte>()) : null)"
            : $"global::System.Convert.ToBase64String({value}.Packed ?? global::System.Array.Empty<byte>())";

    /// <summary>The <c>ProtectedValue</c> whose packed bytes are the Base64 <paramref name="text" />.</summary>
    internal static string ProtectedFromText(string text)
        => $"new global::{Pragmatic.SourceGenerator.Core.ProtectedValueType.FullName}(global::System.Convert.FromBase64String({text}))";

    public static string Literal(string text) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, quote: true);
}
