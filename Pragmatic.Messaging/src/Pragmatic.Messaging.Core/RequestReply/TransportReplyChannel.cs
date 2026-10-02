using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     The REQUESTER side of distributed request/reply: owns one process-unique reply queue on
///     the transport and correlates incoming replies to pending requests by request id.
///     Singleton — the subscription is created lazily on the first request and reused.
/// </summary>
public sealed partial class TransportReplyChannel(
    IMessageTransport transport,
    ILogger<TransportReplyChannel> logger,
    // Overridable for pre-provisioned brokers (the ASB emulator cannot create entities at
    // runtime: a random reply queue name could never be provisioned up front).
    string? replyQueueName = null) : IAsyncDisposable
{
    /// <summary>Header carrying the reply queue name on the request.</summary>
    public const string ReplyToHeader = "x-reply-to";

    /// <summary>Header correlating a reply to its pending request.</summary>
    public const string RequestIdHeader = "x-request-id";

    /// <summary>Header carrying the responder's error message (reply payload is empty).</summary>
    public const string ErrorHeader = "x-rpc-error";

    private readonly ConcurrentDictionary<string, TaskCompletionSource<(byte[] Payload, MessageContext Context)>> _pending = new();
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);
    private IAsyncDisposable? _subscription;

    /// <summary>This process's reply queue (globally unique unless overridden).</summary>
    public string ReplyQueue { get; } = replyQueueName ?? $"replies.{Guid.NewGuid():N}";

    /// <summary>
    ///     Sends nothing itself — registers a pending request and returns the awaitable reply.
    ///     Callers must stamp <see cref="ReplyToHeader"/>/<see cref="RequestIdHeader"/> on the
    ///     outgoing request and send it AFTER calling this (no reply can be missed).
    /// </summary>
    public async Task<Task<(byte[] Payload, MessageContext Context)>> RegisterAsync(string requestId, CancellationToken ct)
    {
        await EnsureSubscribedAsync(ct).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<(byte[], MessageContext)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        return tcs.Task;
    }

    /// <summary>Drops a pending request (timeout/cancellation).</summary>
    public void Forget(string requestId) => _pending.TryRemove(requestId, out _);

    private async Task EnsureSubscribedAsync(CancellationToken ct)
    {
        if (_subscription is not null)
            return;

        await _subscribeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _subscription ??= await RequestReplyBinder.SubscribeQueueAsync(transport, ReplyQueue, (payload, context, _) =>
            {
                var requestId = context.Headers is not null
                    && context.Headers.TryGetValue(RequestIdHeader, out var id)
                        ? id
                        : null;

                if (requestId is not null && _pending.TryRemove(requestId, out var tcs))
                    tcs.TrySetResult((payload.ToArray(), context));
                else
                    LogUnmatchedReply(requestId ?? "(none)");

                return Task.CompletedTask;
            }, ct).ConfigureAwait(false);

            LogReplyChannelOpen(ReplyQueue);
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
            await _subscription.DisposeAsync().ConfigureAwait(false);
        _subscribeLock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reply channel open on {ReplyQueue}")]
    private partial void LogReplyChannelOpen(string replyQueue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unmatched reply (request id: {RequestId}) — request timed out or belongs to another instance")]
    private partial void LogUnmatchedReply(string requestId);
}
