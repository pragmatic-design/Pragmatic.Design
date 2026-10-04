---
title: "Server-Sent Events (streaming endpoints)"
description: "`StreamingEndpoint<TItem>` streams items to the client as SSE (`text/event-stream`):"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/sse-streaming.md
sidebar:
  order: 15
---
`StreamingEndpoint<TItem>` streams items to the client as SSE (`text/event-stream`):
one `data:` event per item, flushed immediately.

```csharp
[Endpoint(HttpVerb.Get, "/api/reservations-feed")]
[Sse(HeartbeatSeconds = 15)]                       // optional idle keep-alive comment
public partial class FeedEndpoint : StreamingEndpoint<FeedItemDto, NotFoundError>
{
    public override async IAsyncEnumerable<Result<FeedItemDto, NotFoundError>> HandleAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (feedMissing)
        {
            yield return NotFoundError.For<string>("Feed", id);   // PRE-stream → HTTP 404
            yield break;
        }

        await foreach (var item in source.WithCancellation(ct))
            yield return item;                                    // data: {json}
    }
}
```

## Error semantics: first-item peek

- A failure yielded **first** becomes a normal HTTP error response (ProblemDetails with the
  error's status code): the stream never opens. Use it for not-found/precondition checks.
- A failure yielded **after** an item cannot change the status (already `200`): it is sent
  as a terminal `event: error` with a ProblemDetails-like payload
  (`{ code, title, detail, status }`) and the stream closes (the iterator's `finally` runs).
- Unhandled exceptions mid-stream are logged and emit `event: error` with status 500.

## Streaming domain actions

`StreamingDomainAction<TItem>` streams through the Actions pipeline: validation and
authorization filters run **before** the stream opens (a short-circuit becomes a normal
HTTP error). Streaming actions are read-oriented: SaveChanges/`[Raises]` run when the
stream is handed over, not when it finishes.

```csharp
[DomainAction]
[Endpoint(HttpVerb.Get, "/api/availability-scan")]
public partial class ScanAvailabilityAction : StreamingDomainAction<SlotDto>
{
    public override async IAsyncEnumerable<Result<SlotDto, IError>> ExecuteStream(
        [EnumeratorCancellation] CancellationToken ct = default) { ... }
}
```

## Backpressure, cancellation, consumption

- **Pull-based backpressure**: the next item is requested only after the previous one has
  been flushed to the transport (the awaited flush *is* the backpressure). A bounded
  `Channel<T>` (`channel.Reader.ReadAllAsync(ct)`) gives producer-side backpressure for free.
- The `ct` is the request-aborted token: enumeration stops when the client disconnects.
- Consume from browsers with `EventSource` (GET) or `fetch` + stream reader; from .NET with
  `System.Net.ServerSentEvents.SseParser`.

## Constraints (compile-time)

| Diagnostic | Rule |
|------------|------|
| PRAG0520 (E) | `[ResponseCache]` on streaming: SSE is not cacheable |
| PRAG0521 (E) | Verb must be GET (EventSource-compatible) or POST |
| PRAG0522 (E) | `[HttpStatus]`/`[CreatedAt]`: success is always 200 |
| PRAG0523 (W) | Versioned handler methods not supported (default version only) |
| PRAG0524 (E) | `[PostProcessor]`: there is no final result to observe |

Not in scope (documented limits): `Last-Event-ID`/resume, custom per-item event names,
streaming Mutation/Query. Exclude `text/event-stream` from response compression (it buffers).
With `PragmaticGenerateJsonContext`/`PublishAot`, the item type is included automatically in
the generated JSON context (AOT-safe serialization through the host options seam).
