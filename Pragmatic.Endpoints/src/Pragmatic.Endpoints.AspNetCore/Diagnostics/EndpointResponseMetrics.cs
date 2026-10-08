using System.Diagnostics.Metrics;

namespace Pragmatic.Endpoints.Diagnostics;

/// <summary>
///     How the JSON responses of generated endpoints were written: by the generated writer of their type, or by the
///     serializer.
/// </summary>
/// <remarks>
///     A host whose response options stop being the ones the generated entry point wrote — a step that adds a
///     converter, a naming policy set in <c>Configure</c> — keeps answering correctly, through the serializer, and
///     loses the generated path without a word. This is the word: the share tagged <c>serializer</c> grows.
/// </remarks>
public static class EndpointResponseMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Pragmatic.Endpoints";

    /// <summary>The tag that says which writer wrote a response.</summary>
    public const string WriterTag = "writer";

    /// <summary>The meter.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>JSON responses of endpoints that have a generated writer, tagged by the writer that wrote them.</summary>
    public static readonly Counter<long> JsonResponses = Meter.CreateCounter<long>(
        "pragmatic.endpoints.json_responses",
        unit: "{responses}",
        description: "JSON responses of endpoints with a generated writer, by the writer that wrote them (generated or serializer)");
}
