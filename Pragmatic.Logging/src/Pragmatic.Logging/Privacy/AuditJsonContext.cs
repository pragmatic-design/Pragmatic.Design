using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Logging.Privacy;

/// <summary>
///     Source-generated JSON metadata for audit entries: the JSON Lines store and the JSON export.
/// </summary>
/// <remarks>
///     <para>
///         camelCase, enums as numbers: what the reflection-based options these replace wrote.
///     </para>
///     <para>
///         <see cref="AuditEntry.CustomProperties" /> holds <c>object</c> values, written by their runtime
///         type, so every type that may land there needs metadata here. The framework writes strings; the
///         scalars are listed for an application's own, and <see cref="JsonElement" /> is what a value read
///         back from the store is. A type not listed fails that entry's serialization, as it would under
///         Native AOT anyway, rather than being reflected over.
///     </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuditEntry))]
[JsonSerializable(typeof(IReadOnlyList<AuditEntry>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class AuditJsonContext : JsonSerializerContext;
