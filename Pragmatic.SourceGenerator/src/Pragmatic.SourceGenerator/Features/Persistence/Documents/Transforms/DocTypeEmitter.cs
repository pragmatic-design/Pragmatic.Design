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
        var fields = model.Fields.Select(f => new DocumentFieldModel(
            f.Property.Name,
            Literal(f.Name),
            WriteField(f, "e." + f.Property.Name),
            ReadField(f),
            Literal(DocModel.JsonName(f)),
            DocJsonEmitter.WriteField(f, "e." + f.Property.Name),
            f.Name == "_id" ? DocJsonEmitter.ParseId(f) : DocJsonEmitter.ReadField(f),
            f.Name == "_id")).ToEquatableArray();
        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        if (model.IsEmbedded)
            return new DocumentTypeModel(hint, ns, type.Name, name, contract, IsEmbedded: true, null, fields, null, false, "0", "", null, null, null, (families ?? []).ToEquatableArray());

        var key = model.Key!;
        var version = model.Version;
        var partition = model.PartitionKey!;
        return new DocumentTypeModel(
            hint, ns, type.Name, name, contract, IsEmbedded: false,
            Literal(model.Collection!),
            fields,
            WriteField(key, "e." + key.Property.Name, "name"),
            version is not null,
            version is null ? "0" : "unchecked((long)e." + version.Property.Name + ")",
            version is null ? "" : $"e.{version.Property.Name} = unchecked(({version.ScalarName})version); ",
            DocJsonEmitter.IdText(key, "e." + key.Property.Name),
            Literal("/" + DocModel.JsonName(partition)),
            DocJsonEmitter.PartitionKeyWrite(key, partition),
            (families ?? []).ToEquatableArray());
    }

    /// <summary>The statement that writes one field's value under its name (or under <paramref name="nameExpression" />, a C# expression).</summary>
    public static string WriteField(DocField field, string value, string? nameExpression = null)
    {
        var name = nameExpression ?? Literal(field.Name);
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
            default:
                return $"{Runtime}.Conversion.DocValues.Write(w, {name}, {value})";
        }
    }

    private static string ReadField(DocField field)
    {
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
        DocKind.Enum => $"({typeName}){Runtime}.Conversion.DocMapping.ReadInt32(ref {reader})",
        _ => $"default! /* {kind} */",
    };

    public static string Literal(string text) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, quote: true);
}
