using System.Text.Json.Serialization;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Serialization;

/// <summary>
///     Source-generated metadata for the workloads, read under the host's options.
/// </summary>
/// <remarks>
///     The stand-in for the context Pragmatic generates for response types: metadata, not a serialization
///     handler. Under the host's options (a converter, reference handling) STJ could not use a handler
///     anyway, which is the point the issue makes, so what this measures is the metadata-driven path an
///     opted-in host takes. It is STJ's metadata, not Pragmatic's own: the shapes here are not response
///     types of an endpoint, so the Pragmatic generator has no reason to cover them.
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Twitter.Root), TypeInfoPropertyName = nameof(Twitter))]
[JsonSerializable(typeof(CitmCatalog.Root), TypeInfoPropertyName = nameof(CitmCatalog))]
[JsonSerializable(typeof(Canada.Root), TypeInfoPropertyName = nameof(Canada))]
[JsonSerializable(typeof(ReservationPage))]
internal sealed partial class HostMetadataContext : JsonSerializerContext;
