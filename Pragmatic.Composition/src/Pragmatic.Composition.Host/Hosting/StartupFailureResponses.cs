using System.Text.Json.Serialization;

namespace Pragmatic.Composition.Hosting;

/// <summary>The health shape reported while the application is down after a failed start.</summary>
/// <param name="Status">Always "Unhealthy" here.</param>
/// <param name="Description">What happened, in one line.</param>
/// <param name="Error">The exception's type name.</param>
/// <param name="Message">The exception's message.</param>
public sealed record StartupFailureHealthResponse(
    string Status,
    string Description,
    string Error,
    string Message);

/// <summary>An exception as the maintenance endpoint renders it.</summary>
/// <param name="Type">
///     The fully-qualified name when diagnostics are on, the short name otherwise — the long form
///     leaks namespace and assembly structure to anyone who can reach the endpoint.
/// </param>
/// <param name="Message">The exception's message.</param>
/// <param name="StackTrace">Only when diagnostics are on.</param>
/// <param name="InnerException">The inner exception, rendered the same way.</param>
public sealed record StartupFailureError(
    string? Type,
    string Message,
    string? StackTrace,
    StartupFailureInnerError? InnerException);

/// <summary>An inner exception, without a stack trace of its own.</summary>
/// <param name="Type">Short or fully-qualified, by the same rule as the outer one.</param>
/// <param name="Message">The exception's message.</param>
public sealed record StartupFailureInnerError(string? Type, string Message);

/// <summary>The maintenance shape reported after a failed start.</summary>
/// <param name="Status">Always "Maintenance" here.</param>
/// <param name="Reason">Always "Startup failure" here.</param>
/// <param name="Error">What went wrong.</param>
/// <param name="Suggestions">What the operator might try.</param>
public sealed record StartupFailureMaintenanceResponse(
    string Status,
    string Reason,
    StartupFailureError Error,
    IReadOnlyList<string> Suggestions);

/// <summary>Where an operator can go while the application is down.</summary>
/// <param name="Health">The health path.</param>
/// <param name="Maintenance">The maintenance path.</param>
/// <param name="Panel">The admin panel path.</param>
/// <param name="Restart">The restart path.</param>
public sealed record StartupFailureEndpoints(string Health, string Maintenance, string Panel, string Restart);

/// <summary>The catch-all answer while the application is down.</summary>
/// <param name="Status">Always "Service Unavailable" here.</param>
/// <param name="Message">What happened.</param>
/// <param name="Endpoints">Where to go next.</param>
public sealed record StartupFailureDefaultResponse(
    string Status,
    string Message,
    StartupFailureEndpoints Endpoints);

/// <summary>
///     JSON metadata for the pages served after a failed start.
/// </summary>
/// <remarks>
///     Named types rather than anonymous objects: an anonymous type cannot appear in a
///     <c>[JsonSerializable]</c> — there is no name to write — so a published AOT binary would have no
///     metadata for it, and the one page whose whole job is to explain why the app is down would
///     itself fail to render.
/// </remarks>
[JsonSerializable(typeof(StartupFailureHealthResponse))]
[JsonSerializable(typeof(StartupFailureMaintenanceResponse))]
[JsonSerializable(typeof(StartupFailureDefaultResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class StartupFailureJsonContext : JsonSerializerContext;
