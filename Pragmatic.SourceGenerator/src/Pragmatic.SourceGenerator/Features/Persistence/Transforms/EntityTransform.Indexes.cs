using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

internal static partial class EntityTransform
{
    /// <summary>The attribute that declares a non-unique index (#524).</summary>
    internal const string IndexAttributeName = "Pragmatic.Persistence.Entity.IndexAttribute";

    /// <summary>
    ///     The non-unique indexes declared with <c>[Index]</c>, in the order the attributes appear, each with its columns as written. A name
    ///     that matches no property is kept, as <see cref="CollectUniqueIndexes" /> keeps it: <c>PRAG0627</c> says which name was wrong.
    /// </summary>
    internal static EquatableArray<IndexModel> CollectIndexes(INamedTypeSymbol typeSymbol)
        => typeSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == IndexAttributeName && a.ConstructorArguments.Length > 0)
            .Select(a => a.ConstructorArguments[0].Values.Select(v => v.Value as string).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToImmutableArray())
            .Where(columns => columns.Length > 0)
            .Select(columns => new IndexModel { Columns = columns })
            .ToImmutableArray();
}
