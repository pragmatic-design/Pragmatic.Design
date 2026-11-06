using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

internal static partial class SqlInterceptorGenerator
{
    internal sealed partial class Projection
    {
        private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";

        /// <summary>
        ///     A member the mapping converts (#563), as the documents engine reads one (#537): the column read with its own type, then what the
        ///     generated mapping applies to it after the read: the <c>[MapConverter&lt;T&gt;]</c>'s <c>Convert</c>, or
        ///     <c>ToString(Format, InvariantCulture)</c> for a <c>[MapProperty(Format = …)]</c>. Null when the member has neither; empty, with
        ///     <paramref name="problem" />, when it has one the projection cannot apply.
        /// </summary>
        /// <remarks>Read with the DTO property's type instead, the column of a date or an amount came back raw, or failed: PostgreSQL refuses to read a timestamp as text.</remarks>
        private static string? Converted(IPropertySymbol property, AttributeData? map, ColumnModel source, out string? problem)
        {
            problem = null;
            var converter = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass is { Name: "MapConverterAttribute", IsGenericType: true, TypeArguments.Length: 1 })?.AttributeClass!.TypeArguments[0];
            var format = map?.NamedArguments.FirstOrDefault(n => n.Key == "Format").Value.Value as string;
            if (converter is null && format is null)
                return null;

            var read = source.ProjectedReader(source.Type);
            if (read is null)
            {
                problem = $"{property.Name} converts {source.Property}, of type {source.Type.ToDisplayString()}, which is not read";
                return "";
            }

            if (converter is not null)
                return $"new {converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}().Convert({read})";

            if (source.Type.NullableAnnotation == NullableAnnotation.Annotated || source.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                problem = $"{property.Name} formats {source.Property}, which may be null: the projection formats a value that is always there";
                return "";
            }

            if (!source.Type.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable"))
            {
                problem = $"{property.Name} formats {source.Property}, of type {source.Type.ToDisplayString()}, which is not IFormattable";
                return "";
            }

            return $"({read}).ToString({SymbolDisplay.FormatLiteral(format!, quote: true)}, {Invariant})";
        }
    }
}
