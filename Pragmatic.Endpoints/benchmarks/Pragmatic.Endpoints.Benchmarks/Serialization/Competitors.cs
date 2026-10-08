using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Serialization;
using REDox;
using REDox.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Serialization;

/// <summary>The serializer configurations the benchmark compares, each built once.</summary>
internal static class Competitors
{
    /// <summary>
    ///     What a Pragmatic host answers with today: ASP.NET's web defaults plus what the generated entry
    ///     point sets (<c>PragmaticEntryTemplate.RenderJsonDefaults</c>).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         camelCase, nulls left out, enums by name through <see cref="JsonStringEnumConverter" />, cycles
    ///         ignored, and the resolver of the <see cref="PragmaticJsonOptions" /> seam. The converter and the
    ///         reference handling are each enough on their own to keep STJ off its serialization handler.
    ///     </para>
    ///     <para>
    ///         ⚠️ Built on ASP.NET's <c>JsonOptions</c>, not on <c>JsonSerializerDefaults.Web</c>: the two differ in
    ///         the encoder. ASP.NET's leaves non-ASCII text and the HTML-sensitive characters unescaped, and the
    ///         first run of this benchmark (2026-10-05) measured the host with the default one, escaping every
    ///         non-ASCII character of twitter.json as <c>\uXXXX</c>.
    ///     </para>
    /// </remarks>
    public static JsonSerializerOptions Host(IJsonTypeInfoResolver resolver)
    {
        var options = new JsonSerializerOptions(new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            TypeInfoResolver = resolver,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>The seam a JIT host builds with nothing registered: the framework's contexts, then reflection.</summary>
    public static IJsonTypeInfoResolver Seam { get; } = new PragmaticJsonOptions().Build().TypeInfoResolver!;

    /// <summary>The host's options over the seam alone: reflection, for a type no context covers.</summary>
    public static JsonSerializerOptions HostReflection { get; } = Host(Seam);

    /// <summary>The host's options with generated metadata in front of the seam: an opted-in host.</summary>
    public static JsonSerializerOptions HostGeneratedMetadata { get; } =
        Host(JsonTypeInfoResolver.Combine(HostMetadataContext.Default, Seam));

    /// <summary>
    ///     RE:Dox configured to the host's contract: camelCase and nulls left out on write.
    /// </summary>
    /// <remarks>
    ///     Its escaping is its own (<see cref="SerializerSettings.TextEncoderPolicy" />), which the
    ///     equivalence check tolerates because it compares the parsed documents, not the bytes.
    /// </remarks>
    public static SerializerSettings REDox { get; } = new DoxSerializerSettings
    {
        PropertyNamingPolicy = NamingPolicy.CamelCase,
        NullValueHandling = NullValueHandling.IgnoreWrite,
    };
}
