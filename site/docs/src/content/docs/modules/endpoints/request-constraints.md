---
title: "Request constraints & OpenAPI examples"
description: "`[MaxBodySize(bytes)]` rejects oversized requests with **413** *before the body is read*"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/request-constraints.md
sidebar:
  order: 13
---
## Max request body size

`[MaxBodySize(bytes)]` rejects oversized requests with **413** *before the body is read*
(`RequestLimitsStep`, auto-registered when Endpoints is detected). Distinct from
`[MaxFileSize]`, which validates individual uploaded files after multipart parsing.

```csharp
[Endpoint(HttpVerb.Post, "/api/booking-notes")]
[MaxBodySize(256)]
public partial class CreateBookingNoteEndpoint : Endpoint<string> { ... }
```

- Global default via configuration: `Pragmatic:RequestLimits:MaxBodySizeBytes`.
- Endpoint metadata wins over the global default.
- Non-positive limits are a compile error (**PRAG0517**).
- Enforced both through `IHttpMaxRequestBodySizeFeature` (Kestrel) and an explicit
  `Content-Length` check — so it also holds on test servers.

## Antiforgery

Form endpoints get `DisableAntiforgery()` by default (API-first). `[RequireAntiforgery]`
keeps token validation **on** for browser form/cookie scenarios:

```csharp
[Endpoint(HttpVerb.Post, "/api/feedback-form")]
[RequireAntiforgery]
public partial class SubmitFeedbackFormEndpoint : Endpoint<string>
{
    [FromForm] public string Message { get; set; } = "";
}
```

With Pragmatic Composition the antiforgery services and middleware are wired automatically
(`AntiforgeryStep`, Order 80 — after auth, the token is user-bound). In library mode call
`AddAntiforgery()` + `UseAntiforgery()` yourself. Requests without a valid token → **400**.

## OpenAPI request/response examples

`[RequestExample]` / `[ResponseExample]` attach JSON examples to the OpenAPI document
(both the compile-time spec and the runtime-enriched one). Repeat for multiple named
examples (Swagger UI dropdown). Invalid JSON warns at compile time (**PRAG0518**).

```csharp
[Endpoint(HttpVerb.Post, "/api/guests")]
[RequestExample("""{ "name": "Ada", "email": "ada@example.com" }""", Name = "minimal")]
[RequestExample("""{ "name": "Grace", "vip": true }""", Name = "vip", Summary = "VIP guest")]
[ResponseExample(201, """{ "id": "00000000-0000-0000-0000-000000000001" }""")]
[ResponseExample(409, """{ "code": "guest.duplicate" }""")]
public partial class CreateGuestMutation : Mutation<Guest> { ... }
```

## HEAD and OPTIONS

`HttpVerb.Head` / `HttpVerb.Options` map via `MapMethods`. HEAD responses must not carry a
body (RFC 9110): declare a `VoidEndpoint` — a HEAD endpoint with a response type warns
(**PRAG0514**) and the generated handler suppresses the body. OPTIONS gets no special
treatment (CORS preflight stays with the CORS middleware).
