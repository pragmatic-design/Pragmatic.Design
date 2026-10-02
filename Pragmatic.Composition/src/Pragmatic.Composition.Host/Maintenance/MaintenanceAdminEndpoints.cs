using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Admin endpoints for maintenance mode: status, SSE stream, health, panel, apply, restart.
///     Auth: X-Maintenance-Key header or localhost-only when no key configured.
/// </summary>
public static class MaintenanceAdminEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///     Maps the maintenance admin endpoints under <see cref="MaintenanceModeOptions.AdminPath"/>:
    ///     status (<c>GET ""</c>), SSE progress stream (<c>GET /stream</c>), health (<c>GET /health</c>),
    ///     HTML panel (<c>GET /panel</c>), and restart (<c>POST /restart</c>). All routes in the group
    ///     are protected by the auth filter built from <paramref name="options"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <param name="options">Maintenance options supplying the admin path and auth configuration.</param>
    public static void Map(IEndpointRouteBuilder endpoints, MaintenanceModeOptions options)
    {
        var group = endpoints.MapGroup(options.AdminPath)
            .AddEndpointFilter(CreateAuthFilter(options))
            .ExcludeFromDescription();

        // RequestDelegate, not a handler Delegate: ASP.NET binds the latter through a reflection-based
        // factory that does not survive a Native AOT publish — and a single endpoint mapped that way
        // fails the construction of the WHOLE routing table, so an app would break just for switching
        // the maintenance panel on.
        group.MapGet("", Execute(services => HandleStatus(
            services.GetRequiredService<IMaintenanceMode>(),
            services.GetRequiredService<MigrationProgressStream>(),
            options)));

        group.MapGet("/stream", (RequestDelegate)(context => HandleStream(
            context,
            context.RequestServices.GetRequiredService<IMigrationProgressStream>())));

        group.MapGet("/health", Execute(services => HandleHealth(
            services.GetRequiredService<IMaintenanceMode>())));

        group.MapGet("/panel", Execute(_ => HandlePanel()));

        group.MapPost("/restart", Execute(services => HandleRestart(
            services.GetRequiredService<IHostApplicationLifetime>())));
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> CreateAuthFilter(
        MaintenanceModeOptions options)
    {
        return async (context, next) =>
        {
            var httpContext = context.HttpContext;

            if (options.AdminApiKey is not null)
            {
                var key = httpContext.Request.Headers["X-Maintenance-Key"].ToString();
                if (!FixedTimeKeyEquals(key, options.AdminApiKey))
                    return Results.Json(
                        new MaintenanceErrorResponse("Invalid or missing X-Maintenance-Key"),
                        MaintenanceAdminJsonContext.Default.MaintenanceErrorResponse,
                        statusCode: 401);
            }
            else
            {
                // No API key configured — localhost only
                var remoteIp = httpContext.Connection.RemoteIpAddress;
                if (remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
                    return Results.Json(
                        new MaintenanceErrorResponse("Admin endpoints restricted to localhost"),
                        MaintenanceAdminJsonContext.Default.MaintenanceErrorResponse,
                        statusCode: 403);
            }

            return await next(context).ConfigureAwait(false);
        };
    }

    /// <summary>
    ///     Constant-time API-key comparison. Both inputs are SHA-256 hashed first so the comparison is
    ///     timing-safe regardless of length (a raw <c>CryptographicOperations.FixedTimeEquals</c>
    ///     would short-circuit on differing lengths and leak the key length).
    /// </summary>
    private static bool FixedTimeKeyEquals(string provided, string expected)
    {
        Span<byte> providedHash = stackalloc byte[32];
        Span<byte> expectedHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), providedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedHash);
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    private static IResult HandleStatus(
        IMaintenanceMode maintenanceMode,
        MigrationProgressStream progressStream,
        MaintenanceModeOptions options)
    {
        var response = new MaintenanceStatusResponse(
            maintenanceMode.IsActive,
            maintenanceMode.Reason,
            maintenanceMode.ActivatedAt,
            maintenanceMode.EstimatedEnd,
            options.IncludeDetailedProgress ? progressStream.GetHistory() : null);

        return Results.Json(response, MaintenanceAdminJsonContext.Default.MaintenanceStatusResponse);
    }

    private static async Task HandleStream(
        HttpContext context,
        IMigrationProgressStream progressStream)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var ct = context.RequestAborted;

        try
        {
            await foreach (var evt in progressStream.StreamAsync(ct).ConfigureAwait(false))
            {
                var eventType = evt.IsError ? "error" : evt.Phase == "complete" ? "complete" : "progress";
                var data = JsonSerializer.Serialize(evt, MaintenanceAdminJsonContext.Default.MigrationProgressEvent);

                await context.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n", ct).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
    }

    private static IResult HandleHealth(IMaintenanceMode maintenanceMode)
    {
        var response = new MaintenanceHealthResponse(
            maintenanceMode.IsActive ? "Maintenance" : "Healthy",
            maintenanceMode.IsActive,
            maintenanceMode.Reason);

        var statusCode = maintenanceMode.IsActive
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        return Results.Json(response, MaintenanceAdminJsonContext.Default.MaintenanceHealthResponse, statusCode: statusCode);
    }

    private static IResult HandlePanel()
    {
        return Results.Content(MaintenancePanel.Html, "text/html");
    }

    private static IResult HandleRestart(IHostApplicationLifetime lifetime)
    {
        lifetime.StopApplication();
        return Results.Json(
            new MaintenanceMessageResponse("Application restart initiated"),
            MaintenanceAdminJsonContext.Default.MaintenanceMessageResponse);
    }

    /// <summary>
    ///     Wraps a handler that returns an <see cref="IResult" /> into a <see cref="RequestDelegate" />.
    /// </summary>
    /// <remarks>
    ///     The services come from the request rather than from the handler's parameters, because
    ///     parameter binding is the part ASP.NET cannot do for a generated or hand-mapped endpoint under
    ///     AOT. Resolving three services by hand is a smaller price than a routing table that refuses
    ///     to build.
    /// </remarks>
    private static RequestDelegate Execute(Func<IServiceProvider, IResult> handler)
        => context => handler(context.RequestServices).ExecuteAsync(context);
}
