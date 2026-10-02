using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Maintenance mode handler for startup failures.
///     Provides /health, /maintenance, and /admin/maintenance/panel endpoints
///     when the application fails to start properly.
/// </summary>
public sealed partial class MaintenanceMode
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly bool _isDevelopment;
    private readonly MaintenanceModeOptions _options;
    private readonly Exception _startupException;

    /// <summary>Creates the maintenance handler that serves the fallback endpoints after a startup failure.</summary>
    /// <param name="startupException">The exception that caused startup to fail. Required.</param>
    /// <param name="options">Maintenance options supplying the endpoint paths and disclosure settings.</param>
    /// <param name="isDevelopment">Whether the host is running in development (controls stack-trace disclosure).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startupException" /> or <paramref name="options" /> is null.</exception>
    public MaintenanceMode(Exception startupException, MaintenanceModeOptions options, bool isDevelopment)
    {
        _startupException = startupException ?? throw new ArgumentNullException(nameof(startupException));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _isDevelopment = isDevelopment;
    }

    /// <summary>
    ///     Always <c>true</c>: this wrapper is only ever constructed to serve the maintenance page
    ///     after a startup failure (<see cref="_startupException"/> is required and non-null). The
    ///     property exists for symmetry with the normal-host status surface, not as a runtime toggle.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Public instance contract property; constant by design for the maintenance wrapper and must not become a static API member.")]
    public bool IsInMaintenanceMode => true;

    /// <summary>The exception that caused the startup failure this maintenance handler is serving.</summary>
    public Exception? StartupException => _startupException;

    /// <summary>
    ///     Wires the terminal request handler that serves the health, maintenance, panel, and restart
    ///     endpoints. Intended as the entire pipeline when the application failed to start normally.
    /// </summary>
    /// <param name="app">The application builder to attach the terminal handler to.</param>
    public void Configure(IApplicationBuilder app)
    {
        AnnounceOnceListening(app.ApplicationServices);

        app.Run(async context =>
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            var adminPath = _options.AdminPath.ToLowerInvariant();

            if (path == _options.HealthPath.ToLowerInvariant())
                await HandleHealthEndpoint(context).ConfigureAwait(false);
            else if (path == _options.MaintenancePath.ToLowerInvariant())
                await HandleMaintenanceEndpoint(context).ConfigureAwait(false);
            else if (path == $"{adminPath}/panel")
                await HandlePanelEndpoint(context).ConfigureAwait(false);
            else if (path == $"{adminPath}/restart" && context.Request.Method == "POST")
                await HandleRestartEndpoint(context).ConfigureAwait(false);
            else
                await HandleDefaultEndpoint(context).ConfigureAwait(false);
        });
    }

    /// <summary>
    ///     Says, once the page is listening, that the host failed to start and where the page answers.
    /// </summary>
    /// <remarks>
    ///     Without it the log of a host that fell back here reads like a successful start —
    ///     <c>Now listening on …</c>, <c>Application started.</c> — while every request answers 503.
    ///     The addresses are read after the server has bound, because a port given as 0 is only known
    ///     then.
    /// </remarks>
    private void AnnounceOnceListening(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<MaintenanceMode>();
        var server = services.GetRequiredService<IServer>();

        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() =>
        {
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            LogEnteredMaintenanceMode(
                logger,
                _startupException,
                _startupException.GetType().Name,
                _startupException.Message,
                addresses is { Count: > 0 } ? string.Join(", ", addresses) : "no address the server reported");
        });
    }

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Startup failed ({ExceptionType}: {ExceptionMessage}). The host is in maintenance mode and "
                  + "answers 503 on {Addresses}. Set Pragmatic:MaintenanceMode:EnableOnStartupFailure=false "
                  + "to let the process exit instead.")]
    private static partial void LogEnteredMaintenanceMode(
        ILogger logger, Exception exception, string exceptionType, string exceptionMessage, string addresses);

    private async Task HandleHealthEndpoint(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";

        var response = new StartupFailureHealthResponse(
            "Unhealthy",
            "Application failed to start",
            _startupException.GetType().Name,
            _startupException.Message);

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(response, StartupFailureJsonContext.Default.StartupFailureHealthResponse))
            .ConfigureAwait(false);
    }

    private async Task HandleMaintenanceEndpoint(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";

        var includeStack = _options.IncludeStackTrace ?? _isDevelopment;

        // When diagnostics are NOT enabled (production default), expose only the short type
        // name. The fully-qualified name leaks internal namespace/assembly structure that an
        // attacker can use for fingerprinting — gate it behind the same flag as the stack trace.
        var response = new StartupFailureMaintenanceResponse(
            "Maintenance",
            "Startup failure",
            new StartupFailureError(
                includeStack ? _startupException.GetType().FullName : _startupException.GetType().Name,
                _startupException.Message,
                includeStack ? _startupException.StackTrace : null,
                _startupException.InnerException is not null
                    ? new StartupFailureInnerError(
                        includeStack
                            ? _startupException.InnerException.GetType().FullName
                            : _startupException.InnerException.GetType().Name,
                        _startupException.InnerException.Message)
                    : null),
            GetSuggestions());

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(response, StartupFailureJsonContext.Default.StartupFailureMaintenanceResponse))
            .ConfigureAwait(false);
    }

    private async Task HandleDefaultEndpoint(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";

        var response = new StartupFailureDefaultResponse(
            "Service Unavailable",
            "Application is in maintenance mode due to startup failure",
            new StartupFailureEndpoints(
                _options.HealthPath,
                _options.MaintenancePath,
                $"{_options.AdminPath}/panel",
                $"{_options.AdminPath}/restart"));

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(response, StartupFailureJsonContext.Default.StartupFailureDefaultResponse))
            .ConfigureAwait(false);
    }

    private static async Task HandlePanelEndpoint(HttpContext context)
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(MaintenancePanel.Html).ConfigureAwait(false);
    }

    private async Task HandleRestartEndpoint(HttpContext context)
    {
        // Restrict to localhost or validated API key to prevent unauthenticated remote shutdown
        if (!IsAuthorizedAdminRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(
                    new MaintenanceErrorResponse("Admin restart restricted to localhost or requires X-Maintenance-Key"),
                    MaintenanceAdminJsonContext.Default.MaintenanceErrorResponse)).ConfigureAwait(false);
            return;
        }

        var lifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(
                new MaintenanceMessageResponse("Application restart initiated"),
                MaintenanceAdminJsonContext.Default.MaintenanceMessageResponse)).ConfigureAwait(false);
    }

    private bool IsAuthorizedAdminRequest(HttpContext context)
    {
        // API key takes priority
        if (_options.AdminApiKey is not null)
        {
            var key = context.Request.Headers["X-Maintenance-Key"].ToString();
            return string.Equals(key, _options.AdminApiKey, StringComparison.Ordinal);
        }

        // Fallback: localhost-only (covers IPv4, IPv6 loopback and IPv4-mapped IPv6)
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
            return true; // in-process / unit test

        if (System.Net.IPAddress.IsLoopback(remoteIp))
            return true;

        // Handle IPv4-mapped IPv6 loopback (::ffff:127.0.0.1)
        if (remoteIp.IsIPv4MappedToIPv6)
            return System.Net.IPAddress.IsLoopback(remoteIp.MapToIPv4());

        return false;
    }

    /// <summary>
    ///     Best-effort, heuristic startup-failure suggestions for the maintenance page. Matches on the
    ///     exception message/type to surface common remediations (DB, configuration, DI). This is a UX
    ///     nicety, not a diagnostic contract: an unrecognised error is NOT left without guidance — the
    ///     final fallback always adds "check application logs", so the result is never empty.
    /// </summary>
    private string[] GetSuggestions()
    {
        var suggestions = new List<string>();
        var exceptionType = _startupException.GetType().Name;
        var message = _startupException.Message;

        if (message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("database", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Check database connection string in configuration");
            suggestions.Add("Verify database server is running and accessible");
        }

        if (message.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
            exceptionType.Contains("Configuration"))
        {
            suggestions.Add("Check appsettings.json for missing or invalid values");
            suggestions.Add("Verify environment-specific configuration files");
        }

        if (message.Contains("dependency", StringComparison.OrdinalIgnoreCase) ||
            exceptionType.Contains("Resolve"))
        {
            suggestions.Add("Check service registrations for missing dependencies");
            suggestions.Add("Verify all required services are registered");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("Check application logs for detailed error information");
            suggestions.Add("Review startup configuration and dependencies");
        }

        return [.. suggestions];
    }
}
