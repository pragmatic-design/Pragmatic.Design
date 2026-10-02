using System.Text.Json.Serialization;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>The maintenance status the admin endpoint reports.</summary>
/// <param name="IsActive">Whether maintenance mode is on.</param>
/// <param name="Reason">Why, when one was given.</param>
/// <param name="ActivatedAt">When it started.</param>
/// <param name="EstimatedEnd">When it is expected to end, when known.</param>
/// <param name="Progress">Migration progress so far, when the options ask for it.</param>
public sealed record MaintenanceStatusResponse(
    bool IsActive,
    string? Reason,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? EstimatedEnd,
    IReadOnlyList<MigrationProgressEvent>? Progress);

/// <summary>The health shape the admin endpoint reports.</summary>
/// <param name="Status">"Maintenance" or "Healthy".</param>
/// <param name="IsActive">Whether maintenance mode is on.</param>
/// <param name="Reason">Why, when one was given.</param>
public sealed record MaintenanceHealthResponse(string Status, bool IsActive, string? Reason);

/// <summary>A one-line message, for the endpoints that only acknowledge.</summary>
/// <param name="Message">What happened.</param>
public sealed record MaintenanceMessageResponse(string Message);

/// <summary>What the middleware answers to every request while maintenance is on.</summary>
/// <param name="Status">Always "Service Unavailable" here.</param>
/// <param name="Message">What is happening.</param>
/// <param name="Reason">Why, when one was given.</param>
/// <param name="ActivatedAt">When it started.</param>
/// <param name="EstimatedEnd">When it is expected to end, when known.</param>
public sealed record MaintenanceUnavailableResponse(
    string Status,
    string Message,
    string? Reason,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? EstimatedEnd);

/// <summary>A refusal from the admin auth filter.</summary>
/// <param name="Error">Why the request was refused.</param>
public sealed record MaintenanceErrorResponse(string Error);

/// <summary>
///     JSON metadata for the maintenance admin endpoint's own shapes.
/// </summary>
/// <remarks>
///     These were anonymous objects. An anonymous type has no name to write in a
///     <c>[JsonSerializable]</c> and no metadata a published AOT binary can find, so every response
///     from this panel went through reflection — and the panel is mapped by every host that turns
///     maintenance mode on.
/// </remarks>
[JsonSerializable(typeof(MaintenanceStatusResponse))]
[JsonSerializable(typeof(MaintenanceHealthResponse))]
[JsonSerializable(typeof(MaintenanceMessageResponse))]
[JsonSerializable(typeof(MaintenanceErrorResponse))]
[JsonSerializable(typeof(MaintenanceUnavailableResponse))]
[JsonSerializable(typeof(MigrationProgressEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class MaintenanceAdminJsonContext : JsonSerializerContext;
