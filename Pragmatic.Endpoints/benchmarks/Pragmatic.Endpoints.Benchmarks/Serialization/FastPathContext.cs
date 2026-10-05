using System.Text.Json.Serialization;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Serialization;

/// <summary>
///     STJ's own fastest path: a context in serialization mode, used with its own options.
/// </summary>
/// <remarks>
///     <para>
///         camelCase and nulls left out, the host's contract, set here rather than on a separate options
///         instance so that the generated serialization handler is compatible and used. No converter and
///         no reference handling, which is what the host adds and what makes the handler unusable there.
///     </para>
///     <para>
///         Default mode, handler and metadata both: a type the handler cannot cover alone (Twitter's
///         recursive <c>Status</c>) falls back to metadata, as it would in any application using STJ.
///     </para>
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Twitter.Root), TypeInfoPropertyName = nameof(Twitter))]
[JsonSerializable(typeof(CitmCatalog.Root), TypeInfoPropertyName = nameof(CitmCatalog))]
[JsonSerializable(typeof(Canada.Root), TypeInfoPropertyName = nameof(Canada))]
[JsonSerializable(typeof(ReservationPage))]
internal sealed partial class FastPathContext : JsonSerializerContext;
