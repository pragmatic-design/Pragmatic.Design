using System.Text.Json;
using Pragmatic.Serialization;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     Writes a complex structured value as JSON from the metadata the application's JSON seam knows,
///     never by reflection of its own.
/// </summary>
/// <remarks>
///     <para>
///         The JSON providers used to call <c>JsonSerializer.Serialize(value, options)</c>, which reflects
///         over the value. Under Native AOT that throws inside the provider and the entry is lost. Here
///         the type information comes from the host's <see cref="PragmaticJsonOptions" />: its generated
///         and framework contexts, and on a JIT runtime its reflection fallback, so a JIT host writes
///         what it wrote before.
///     </para>
///     <para>
///         The provider's own options (naming, null handling, encoder) still decide how the value is
///         written; only the resolver is the seam's.
///     </para>
///     <para>
///         ⚠️ A type the seam has no metadata for is written as its <c>ToString()</c> and counted
///         (<see cref="ValuesWithoutMetadata" />), instead of failing the entry.
///     </para>
/// </remarks>
internal sealed class ComplexValueSerializer
{
    private readonly JsonSerializerOptions _options;
    private long _valuesWithoutMetadata;

    /// <param name="providerOptions">How the provider writes JSON.</param>
    /// <param name="seam">The application's JSON seam, or null outside a container.</param>
    public ComplexValueSerializer(JsonSerializerOptions providerOptions, PragmaticJsonOptions? seam)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);

        _options = new JsonSerializerOptions(providerOptions)
        {
            TypeInfoResolver = (seam ?? new PragmaticJsonOptions()).Build().TypeInfoResolver,
        };
    }

    /// <summary>How many values were written as their <c>ToString()</c> for lack of JSON metadata.</summary>
    public long ValuesWithoutMetadata => Interlocked.Read(ref _valuesWithoutMetadata);

    /// <summary>The value as JSON, or as its <c>ToString()</c> when its type has no metadata.</summary>
    public string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            if (_options.TryGetTypeInfo(value.GetType(), out var typeInfo))
                return JsonSerializer.Serialize(value, typeInfo);
        }
        catch (NotSupportedException)
        {
            // Metadata for the type and none for one of its members: the serializer says so only here.
            return WithoutMetadata(value);
        }

        return WithoutMetadata(value);
    }

    private string WithoutMetadata(object value)
    {
        Interlocked.Increment(ref _valuesWithoutMetadata);
        return value.ToString() ?? "";
    }
}
