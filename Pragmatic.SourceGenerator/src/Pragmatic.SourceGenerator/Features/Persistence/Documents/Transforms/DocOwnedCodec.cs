using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A Pragmatic entity's owned types as nested documents. An owned type (<c>[Owned]</c>, <c>[ValueObject]</c>, an identity record such as
///     <c>LocalIdentity</c>) is not this engine's <c>[Embedded]</c> partial: it may live in another assembly, or be a record built through its
///     constructor. So the generator emits a codec for it in the application's assembly, which the entity's reader and writer call, and which
///     uses only the public BSON and JSON writers and readers.
/// </summary>
internal static class DocOwnedCodec
{
    private const string Namespace = "Pragmatic.Persistence.Documents.Generated";

    /// <summary>The codec's class name: the owned type's full name, so two types of one simple name do not share it.</summary>
    public static string Name(INamedTypeSymbol type)
        => new string(type.ToDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + "_DocumentCodec";

    public static string QualifiedName(INamedTypeSymbol type) => $"global::{Namespace}.{Name(type)}";

    /// <summary>
    ///     The owned types the documents of <paramref name="models" /> hold, at any depth, each once, in name order; their problems reported
    ///     (a member that is no field, a type built by no constructor).
    /// </summary>
    public static List<DocumentOwnedCodecModel> For(IEnumerable<DocModel> models, List<Diagnostic> diagnostics)
    {
        var codecs = new List<DocumentOwnedCodecModel>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<DocModel>(models);
        while (pending.Count > 0)
        {
            foreach (var field in pending.Dequeue().Fields.Where(f => f.Owned))
            {
                var type = field.Embedded!;
                if (!seen.Add(type) || DocModel.Read(type, diagnostics) is not { } owned)
                    continue;
                if (Model(owned, field, diagnostics) is { } codec)
                    codecs.Add(codec);
                pending.Enqueue(owned);
            }
        }

        return codecs.OrderBy(c => c.CodecName, System.StringComparer.Ordinal).ToList();
    }

    private static DocumentOwnedCodecModel? Model(DocModel owned, DocField usedBy, List<Diagnostic> diagnostics)
    {
        var type = owned.Type;
        if (owned.Fields.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.UnsupportedMember, usedBy.Property.Locations.FirstOrDefault() ?? Location.None, usedBy.Property.ContainingType.Name, usedBy.Property.Name,
                type.ToDisplayString(), "an owned type with no member the document can hold"));
            return null;
        }

        if (EntityModel.ConstructorMembers(type, owned.Fields.Select(f => f.Property.Name).ToList()) is not { } constructor)
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.UnsupportedMember, usedBy.Property.Locations.FirstOrDefault() ?? Location.None, usedBy.Property.ContainingType.Name, usedBy.Property.Name,
                type.ToDisplayString(), "an owned type has a public parameterless constructor, or one whose parameters are its members"));
            return null;
        }

        var fields = owned.Fields.Select(f => new OwnedCodecFieldModel(
            f.Property.Name,
            f.Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
            DocTypeEmitter.Literal(f.Name),
            DocTypeEmitter.WriteField(f, "e." + f.Property.Name),
            DocTypeEmitter.ReadField(f),
            DocTypeEmitter.Literal(DocModel.JsonName(f)),
            DocJsonEmitter.WriteField(f, "e." + f.Property.Name),
            DocJsonEmitter.ReadField(f))).ToEquatableArray();
        var hint = VirtualFolderHints.ForType(type.Name, "DocumentCodec", type.ContainingNamespace.ToDisplayString());
        return new DocumentOwnedCodecModel(hint, Name(type), type.ToDisplayString(DocModel.Qualified), fields, constructor.ToEquatableArray());
    }
}
