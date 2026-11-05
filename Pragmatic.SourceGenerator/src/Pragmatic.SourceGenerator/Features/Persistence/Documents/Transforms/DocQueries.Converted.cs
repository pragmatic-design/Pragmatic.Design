using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

internal static partial class DocQueries
{
    private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";

    /// <summary>
    ///     A member the mapping converts (#537): the field read, then what the generated mapping (<c>{Dto}.Projection</c>) applies to it after the
    ///     read: the <c>[MapConverter&lt;T&gt;]</c>'s <c>Convert</c>, or <c>ToString(Format, InvariantCulture)</c> for a
    ///     <c>[MapProperty(Format = …)]</c>. Null when the member has neither; empty when it has one the projection cannot apply, with the reason.
    /// </summary>
    private static string? Converted(PragmaticQuery query, IPropertySymbol property, DocField field, string read)
    {
        var value = read + field.Property.Name;
        var converter = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass is { Name: "MapConverterAttribute", IsGenericType: true, TypeArguments.Length: 1 })?.AttributeClass!.TypeArguments[0];
        if (converter is not null)
            return $"new {converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}().Convert({value})";

        var format = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "MapPropertyAttribute")?.NamedArguments
            .FirstOrDefault(n => n.Key == "Format").Value.Value as string;
        if (format is null)
            return null;

        var source = field.Property.Type;
        if (source.NullableAnnotation == NullableAnnotation.Annotated || source.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            query.Refuse($"{query.Result.Name}.{property.Name} formats {field.Property.Name}, which may be null: the projection formats a value that is always there");
            return "";
        }

        if (!source.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable"))
        {
            query.Refuse($"{query.Result.Name}.{property.Name} formats {field.Property.Name}, of type {source.ToDisplayString()}, which is not IFormattable");
            return "";
        }

        return $"{value}.ToString({DocTypeEmitter.Literal(format)}, {Invariant})";
    }
}
