using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql.Conversion;

/// <summary>
///     A column stored as another type than the property's: the two static functions between them. Declared on
///     the property with <see cref="ConvertAttribute{TConverter}" />; the generator calls them in the readers, the
///     binders and the predicates' parameters, so no conversion is looked up at runtime (the repository's
///     <c>UtcTicksConverter</c>, <c>ShortGuid</c>, enum-as-string and value objects are converters of this shape).
/// </summary>
public interface ISqlConverter<TModel, TStore>
{
    static abstract TStore ToStore(TModel value);

    static abstract TModel FromStore(TStore value);
}
