using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Messaging.Dashboard.Dtos;

namespace Pragmatic.Messaging.Dashboard;

/// <summary>
///     Source-generated JSON context for dashboard responses — the dashboard stays
///     reflection-free and AOT-clean like the rest of the runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(DashboardStatusDto))]
[JsonSerializable(typeof(IReadOnlyList<OutboxItemDto>))]
[JsonSerializable(typeof(IReadOnlyList<DeadLetterItemDto>))]
[JsonSerializable(typeof(IReadOnlyList<SagaGroupDto>))]
[JsonSerializable(typeof(IReadOnlyList<AuditItemDto>))]
[JsonSerializable(typeof(OperationResultDto))]
internal sealed partial class DashboardJsonContext : JsonSerializerContext;
