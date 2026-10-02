using System.Text.Json.Serialization;

namespace Pragmatic.Messaging.Sql;

/// <summary>Source-generated JSON context for header persistence (zero reflection).</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SqlTransportJsonContext : JsonSerializerContext;
