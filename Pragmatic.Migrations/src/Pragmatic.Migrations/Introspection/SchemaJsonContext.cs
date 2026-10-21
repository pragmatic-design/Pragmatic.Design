using System.Text.Json.Serialization;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     The metadata of the schema the audit table records (<c>SchemaJson</c>), with the options it has always been
///     written with: camelCase, not indented.
/// </summary>
/// <remarks>
///     Generated rather than reflected, so a host that migrates at startup can be published Native AOT without a
///     warning on the one path every recorded migration takes.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(SchemaVersion))]
internal sealed partial class SchemaJsonContext : JsonSerializerContext;
