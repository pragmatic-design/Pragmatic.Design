using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pragmatic.Serialization;

/// <summary>
///     Source-generated JSON context for the primitive shapes the framework serializes ubiquitously
///     (string maps, string arrays). Seeded into every <see cref="PragmaticJsonOptions"/> as a
///     baseline so these types resolve AOT-safely even with the reflection fallback disabled, and
///     exposed so fixed-type call sites (EF value converters, stores) can serialize via a
///     <c>JsonTypeInfo</c> directly — no options, no reflection.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
// The remote-boundary wire shapes. Both ends of /_pragmatic/invoke are generated code, so if these
// are not covered every cross-boundary call falls back to reflection.
[JsonSerializable(typeof(PragmaticInvokeEnvelope))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class PragmaticCommonJsonContext : JsonSerializerContext;
