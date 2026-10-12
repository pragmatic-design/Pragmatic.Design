using System.Text.Json.Serialization;

namespace Pragmatic.Agent.KV;

/// <summary>
///     The KV snapshot file <see cref="KvFilePersistence" /> writes and loads, with the options the file has always
///     had: camelCase, indented. Generated rather than reflected (#142).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(List<KvFilePersistence.KvEntryDto>))]
internal sealed partial class KvFileJsonContext : JsonSerializerContext;
