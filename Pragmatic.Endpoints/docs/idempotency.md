# Idempotency

`[Idempotent]` makes an unsafe endpoint (POST/PUT/PATCH/DELETE) safe to retry: requests must
carry an idempotency-key header, and the first successful response is **replayed** for
retries with the same key and body — the handler runs once.

```csharp
[Endpoint(HttpVerb.Post, "/api/booking-tokens")]
[Idempotent(DurationSeconds = 300)]                     // replay window (default 1h)
public partial class CreateBookingTokenEndpoint : Endpoint<string>
{
    public required string Purpose { get; set; }
    // ...
}
```

## Behavior

| Scenario | Result |
|----------|--------|
| Header missing | `400` ProblemDetails (`Idempotency-Key` by default) |
| First request | Handler runs; a 2xx response is captured (status, content type, `Location`, body) and cached |
| Retry, same key + same body | Captured response replayed — handler **not** re-executed |
| Same key, different body | Treated as a distinct request (the body hash is part of the cache key) |
| Concurrent same-key requests | The first runs; the others get `409` while it is in flight, and replay once it lands |
| 4xx/5xx | **Not cached** — the client can retry with the same key |

## Configuration

Per endpoint via the attribute (`DurationSeconds`, `HeaderName`), globally via
`PragmaticEndpointsOptions.Idempotency` (`HeaderName`, `DefaultDurationSeconds`).

## Requirements and notes

- **Requires Pragmatic.Caching** (`AddPragmaticCaching()`), category
  `CacheCategories.Idempotency` (falls back to the default stack). The filter **fails fast**
  when the cache is missing — silently losing the guarantee would be a correctness bug.
  With a distributed backend (Redis) the guarantee spans instances.
- The body hash is computed from the **bound body DTO** serialized with the host JSON
  options (endpoint filters run after binding). Form/multipart endpoints hash key + route only.
- `[Idempotent]` on GET/HEAD/OPTIONS warns (**PRAG0513**) and emits nothing — safe verbs are
  idempotent by definition; use `[ResponseCache]` for caching semantics.
- The required header is documented automatically in the manifest and OpenAPI.
- Responses are buffered for capture — keep idempotent endpoints' payloads reasonably small.
- The endpoint runs in the **caller's own execution context**, never inside a cache factory. This is
  what makes `[Idempotent]` usable together with `[RequirePermission]`: inside a `HybridCache`
  factory `IHttpContextAccessor.HttpContext` is null (dotnet/extensions#5648), so `ICurrentUser`
  sees no principal and every authenticated request is refused with `401`. Sharing one execution
  across concurrent callers would be worse than the refusal — one caller's response delivered to
  another — so duplicates are answered with `409` instead of joined.
- ⚠️ The in-flight reservation is atomic **within a process**. Across instances it is only as atomic
  as the registered `ICacheStack`: closing it requires a backend with a native atomic increment
  (Redis `INCR`).
- The key is **mandatory** where the attribute is applied: there is no honour-if-present mode, so
  adopting it on an existing endpoint rewrites the contract for every caller at once.
