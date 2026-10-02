using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Audit;
using Pragmatic.Messaging.Dashboard.Dtos;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Dashboard;

/// <summary>
///     Ops endpoints for messaging: status, outbox, dead-letters (with replay/delete),
///     sagas, audit, and an embedded HTML panel.
///     Auth: X-Messaging-Key header (single or labelled keys) or localhost-only when no key configured.
/// </summary>
public static partial class MessagingDashboardEndpoints
{
    /// <summary>HttpContext.Items slot holding the authenticated caller's key label (for audit logging).</summary>
    private const string KeyLabelItem = "Pragmatic.Messaging.Dashboard.KeyLabel";
    /// <summary>
    ///     Maps the dashboard endpoints under <see cref="MessagingDashboardOptions.Path"/>.
    ///     All routes are protected by the auth filter and excluded from OpenAPI.
    /// </summary>
    public static void Map(IEndpointRouteBuilder endpoints, MessagingDashboardOptions options)
    {
        var group = endpoints.MapGroup(options.Path)
            .AddEndpointFilter(CreateAuthFilter(options))
            .ExcludeFromDescription();

        // Options flow by closure — the dashboard works whether or not EnableDashboard
        // registered them in DI (e.g. when mapped manually).
        group.MapGet("/status", (HttpContext ctx, CancellationToken ct) => HandleStatus(ctx, options, ct));
        group.MapGet("/outbox", (HttpContext ctx, string? boundary, CancellationToken ct) => HandleOutbox(ctx, options, boundary, ct));
        group.MapGet("/dead-letters", (HttpContext ctx, CancellationToken ct) => HandleDeadLetters(ctx, options, ct));
        group.MapPost("/dead-letters/{id:guid}/replay", HandleReplay);
        group.MapDelete("/dead-letters/{id:guid}", HandleDeleteDeadLetter);
        group.MapGet("/sagas", HandleSagas);
        group.MapGet("/audit", (HttpContext ctx, string? type, string? direction, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
            => HandleAudit(ctx, options, type, direction, from, to, ct));
        group.MapGet("/panel", HandlePanel);
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> CreateAuthFilter(
        MessagingDashboardOptions options)
    {
        // The accepted (label, key) set, built once: the single ApiKey (label "default") plus any
        // labelled ApiKeys. Empty => loopback-only mode.
        var keys = new List<KeyValuePair<string, string>>();
        if (options.ApiKey is not null)
            keys.Add(new KeyValuePair<string, string>("default", options.ApiKey));
        foreach (var kv in options.ApiKeys)
            keys.Add(kv);

        return async (context, next) =>
        {
            var httpContext = context.HttpContext;

            if (keys.Count > 0)
            {
                var provided = httpContext.Request.Headers["X-Messaging-Key"].ToString();
                var label = MatchKeyLabel(provided, keys);
                if (label is null)
                    return Results.Json(new OperationResultDto("Invalid or missing X-Messaging-Key"),
                        DashboardJsonContext.Default.OperationResultDto, statusCode: 401);
                httpContext.Items[KeyLabelItem] = label;
            }
            else
            {
                // No API key configured — loopback only, and only when the connection IP can be
                // POSITIVELY trusted as loopback. Deny when it is unknown (null — some socket/proxy
                // setups) and when a forwarding header is present: behind a reverse proxy that ran
                // UseForwardedHeaders, Connection.RemoteIpAddress reflects a client-controlled
                // X-Forwarded-For, so "loopback" is spoofable (send X-Forwarded-For: 127.0.0.1).
                // Exposing the dashboard behind a proxy requires setting ApiKey.
                var remoteIp = httpContext.Connection.RemoteIpAddress;
                var forwarded = httpContext.Request.Headers.ContainsKey("X-Forwarded-For")
                    || httpContext.Request.Headers.ContainsKey("Forwarded");
                if (remoteIp is null || forwarded || !IPAddress.IsLoopback(remoteIp))
                    return Results.Json(new OperationResultDto(
                            "Messaging dashboard restricted to localhost — set ApiKey to expose it behind a proxy"),
                        DashboardJsonContext.Default.OperationResultDto, statusCode: 403);

                // CSRF: in loopback mode there is no key header to force a CORS preflight, so a mutating
                // request (replay/delete) must carry the non-simple CsrfHeader. A cross-site page cannot
                // set it without a preflight that same-origin policy blocks — defeating a drive-by POST
                // to localhost. Safe (GET/HEAD) requests are unaffected.
                if (IsMutating(httpContext.Request.Method)
                    && !httpContext.Request.Headers.ContainsKey(MessagingDashboardOptions.CsrfHeader))
                    return Results.Json(new OperationResultDto(
                            $"Mutating requests require the {MessagingDashboardOptions.CsrfHeader} header in loopback mode (CSRF protection)"),
                        DashboardJsonContext.Default.OperationResultDto, statusCode: 403);

                httpContext.Items[KeyLabelItem] = "loopback";
            }

            return await next(context).ConfigureAwait(false);
        };
    }

    private static bool IsMutating(string method)
        => HttpMethods.IsPost(method) || HttpMethods.IsDelete(method)
            || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method);

    /// <summary>
    ///     Returns the label of the first key that matches, or null if none do. Every candidate is
    ///     compared (no early-out) so timing does not reveal which — or how many — keys matched.
    /// </summary>
    private static string? MatchKeyLabel(string provided, List<KeyValuePair<string, string>> keys)
    {
        string? matched = null;
        foreach (var kv in keys)
            if (FixedTimeKeyEquals(provided, kv.Value))
                matched = kv.Key;
        return matched;
    }

    /// <summary>
    ///     Constant-time API-key comparison; both inputs are SHA-256 hashed first so the
    ///     comparison is timing-safe regardless of length.
    /// </summary>
    private static bool FixedTimeKeyEquals(string provided, string expected)
    {
        Span<byte> providedHash = stackalloc byte[32];
        Span<byte> expectedHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), providedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedHash);
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    /// <summary>The caller label recorded by the auth filter (key label, or "loopback").</summary>
    private static string CallerLabel(HttpContext httpContext)
        => httpContext.Items.TryGetValue(KeyLabelItem, out var label) && label is string s ? s : "unknown";

    private static async Task<IResult> HandleStatus(HttpContext httpContext, MessagingDashboardOptions options, CancellationToken ct)
    {
        var services = httpContext.RequestServices;
        var transport = services.GetService<IMessageTransport>();

        var deadLetters = 0;
        if (services.GetService<IDeadLetterStore>() is { } deadLetterStore)
            deadLetters = (await deadLetterStore.GetAllAsync(ct).ConfigureAwait(false)).Count;

        // InspectPendingAsync is read-only and counts ALL unprocessed rows — it neither leases rows
        // (which would starve the delivery pump on every 5s auto-refresh) nor caps at MaxItems.
        var outboxPending = 0;
        foreach (var source in services.GetServices<IOutboxSource>())
            outboxPending += (await source.InspectPendingAsync(ct).ConfigureAwait(false)).Pending;

        var activeSagas = 0;
        foreach (var descriptor in services.GetServices<SagaDescriptor>())
            activeSagas += (await descriptor.GetActiveAsync(services, ct).ConfigureAwait(false)).Count;

        var status = new DashboardStatusDto(
            transport?.Name ?? "InMemory",
            transport?.Status.ToString() ?? "N/A",
            deadLetters,
            outboxPending,
            activeSagas);

        return Results.Json(status, DashboardJsonContext.Default.DashboardStatusDto);
    }

    private static async Task<IResult> HandleOutbox(HttpContext httpContext, MessagingDashboardOptions options, string? boundary, CancellationToken ct)
    {
        var services = httpContext.RequestServices;
        var items = new List<OutboxItemDto>();

        foreach (var source in services.GetServices<IOutboxSource>())
        {
            if (boundary is not null && !string.Equals(source.BoundaryName, boundary, StringComparison.OrdinalIgnoreCase))
                continue;

            // PeekPendingAsync is read-only (no claim/lease), so browsing the outbox list does not
            // steal rows from the delivery pump.
            foreach (var message in await source.PeekPendingAsync(options.MaxItems, ct).ConfigureAwait(false))
                items.Add(new OutboxItemDto(
                    message.Id, source.BoundaryName, message.MessageType,
                    message.CreatedAt, message.NextAttemptAt, message.RetryCount, message.Error));
        }

        return Results.Json((IReadOnlyList<OutboxItemDto>)items, DashboardJsonContext.Default.IReadOnlyListOutboxItemDto);
    }

    private static async Task<IResult> HandleDeadLetters(HttpContext httpContext, MessagingDashboardOptions options, CancellationToken ct)
    {
        var services = httpContext.RequestServices;

        IReadOnlyList<DeadLetterItemDto> items = [];
        if (services.GetService<IDeadLetterStore>() is { } store)
        {
            var all = await store.GetAllAsync(ct).ConfigureAwait(false);
            items = [.. all
                .OrderByDescending(m => m.FailedAt)
                .Take(options.MaxItems)
                .Select(m => new DeadLetterItemDto(m.Id, m.MessageType, m.Error, m.RetryCount, m.FailedAt, m.Context.CorrelationId))];
        }

        return Results.Json(items, DashboardJsonContext.Default.IReadOnlyListDeadLetterItemDto);
    }

    /// <summary>
    ///     Re-publishes a dead letter through the bus and removes it from the store.
    /// </summary>
    /// <remarks>
    ///     Publish-then-remove are two steps, not one transaction: a crash between them can leave the
    ///     message re-published AND still in the dead-letter store, so a second replay re-publishes it.
    ///     Replay is therefore <b>at-least-once</b> — enable consumer idempotency (<c>EnableIdempotency</c>)
    ///     so a duplicate is deduped. The original MessageId is preserved precisely so idempotency can
    ///     match it (the failed dispatch never marked it processed).
    /// </remarks>
    private static async Task<IResult> HandleReplay(HttpContext httpContext, Guid id, CancellationToken ct)
    {
        var services = httpContext.RequestServices;

        if (services.GetService<IDeadLetterStore>() is not { } store)
            return Results.Json(new OperationResultDto("No dead letter store registered"),
                DashboardJsonContext.Default.OperationResultDto, statusCode: 404);

        var deadLetter = await store.GetAsync(id, ct).ConfigureAwait(false);
        if (deadLetter is null)
            return Results.Json(new OperationResultDto($"Dead letter {id} not found"),
                DashboardJsonContext.Default.OperationResultDto, statusCode: 404);

        // SG-generated switch registries (one per module assembly) — the only
        // reflection-free way back from FQN to type.
        object? message = null;
        foreach (var registry in services.GetServices<IMessageTypeRegistry>())
        {
            message = registry.Deserialize(deadLetter.MessageType, deadLetter.Payload);
            if (message is not null) break;
        }
        if (message is null)
            return Results.Json(new OperationResultDto(
                    $"Type '{deadLetter.MessageType}' not in the message type registry — cannot replay"),
                DashboardJsonContext.Default.OperationResultDto, statusCode: 422);

        // Same MessageId (idempotency stores never marked it processed — the dispatch failed),
        // fresh retry budget.
        var bus = services.GetRequiredService<IMessageBus>();
        var context = deadLetter.Context with { RetryCount = 0 };
        await bus.PublishAsync(message, message.GetType(), context, ct).ConfigureAwait(false);
        await store.RemoveAsync(id, ct).ConfigureAwait(false);

        if (services.GetService<ILoggerFactory>()?.CreateLogger("Pragmatic.Messaging.Dashboard") is { } logger)
            LogReplay(logger, CallerLabel(httpContext), id, deadLetter.MessageType);

        return Results.Json(new OperationResultDto($"Dead letter {id} replayed as {deadLetter.MessageType}"),
            DashboardJsonContext.Default.OperationResultDto);
    }

    private static async Task<IResult> HandleDeleteDeadLetter(HttpContext httpContext, Guid id, CancellationToken ct)
    {
        var services = httpContext.RequestServices;

        if (services.GetService<IDeadLetterStore>() is not { } store ||
            await store.GetAsync(id, ct).ConfigureAwait(false) is null)
            return Results.Json(new OperationResultDto($"Dead letter {id} not found"),
                DashboardJsonContext.Default.OperationResultDto, statusCode: 404);

        await store.RemoveAsync(id, ct).ConfigureAwait(false);

        if (services.GetService<ILoggerFactory>()?.CreateLogger("Pragmatic.Messaging.Dashboard") is { } logger)
            LogDelete(logger, CallerLabel(httpContext), id);

        return Results.Json(new OperationResultDto($"Dead letter {id} deleted"),
            DashboardJsonContext.Default.OperationResultDto);
    }

    private static async Task<IResult> HandleSagas(HttpContext httpContext, CancellationToken ct)
    {
        var services = httpContext.RequestServices;
        var groups = new List<SagaGroupDto>();

        foreach (var descriptor in services.GetServices<SagaDescriptor>())
        {
            var active = await descriptor.GetActiveAsync(services, ct).ConfigureAwait(false);
            groups.Add(new SagaGroupDto(descriptor.Name, descriptor.StateTypeName, active));
        }

        return Results.Json((IReadOnlyList<SagaGroupDto>)groups, DashboardJsonContext.Default.IReadOnlyListSagaGroupDto);
    }

    private static async Task<IResult> HandleAudit(
        HttpContext httpContext,
        MessagingDashboardOptions options,
        string? type,
        string? direction,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var services = httpContext.RequestServices;

        IReadOnlyList<AuditItemDto> items = [];

        // Reads the framework trail rather than a messaging-owned store. Still optional: an application
        // can run messaging without auditing configured, and the panel shows an empty list rather than
        // failing.
        if (services.GetService<IAuditTrailReader>() is { } reader)
        {
            var page = await reader.QueryAsync(new AuditQuery
            {
                Category = AuditCategory.Message,
                TargetType = type,
                Outcome = ParseOutcome(direction),
                From = from,
                Until = to,
                Limit = options.MaxItems,
            }, ct).ConfigureAwait(false);

            items = [.. page.Entries.Select(e => new AuditItemDto(
                e.Seq, e.TargetType ?? string.Empty, e.TargetId, e.Outcome.ToString(), e.OccurredAt, e.Detail))];
        }

        return Results.Json(items, DashboardJsonContext.Default.IReadOnlyListAuditItemDto);
    }

    /// <summary>
    ///     Maps the panel's filter to an outcome, tolerating the words the old trail used.
    /// </summary>
    /// <remarks>
    ///     The query parameter is still called <c>direction</c> and still accepts "Handled"/"Failed":
    ///     renaming it would break every saved link and bookmark to the panel for a cosmetic gain.
    ///     Anything unrecognised means no filter, so a stale bookmark shows everything rather than
    ///     silently showing nothing.
    /// </remarks>
    private static AuditOutcome? ParseOutcome(string? direction) => direction?.ToLowerInvariant() switch
    {
        "handled" or "success" => AuditOutcome.Success,
        "failed" or "failure" => AuditOutcome.Failed,
        "denied" => AuditOutcome.Denied,
        _ => null,
    };

    private static IResult HandlePanel()
        => Results.Content(MessagingDashboardPanel.Html, "text/html");

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Dashboard: caller '{Label}' replayed dead letter {Id} ({MessageType})")]
    private static partial void LogReplay(ILogger logger, string label, Guid id, string messageType);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Dashboard: caller '{Label}' deleted dead letter {Id}")]
    private static partial void LogDelete(ILogger logger, string label, Guid id);
}
