# Pragmatic AOT smoke

Native AOT sanity checks for the serialization seam and the HTTP path. Three samples:

## 1. Seam with a hand-authored context (`Pragmatic.Aot.Smoke`)

`Pragmatic.Aot.Smoke/Program.cs` does two things.

First it asserts that a **fresh** `PragmaticJsonOptions` already has `ReflectionFallbackEnabled == false`.
That default derives from `RuntimeFeature.IsDynamicCodeSupported`, so under Native AOT it must be off
without anyone asking — and every other smoke calls `DisableReflectionFallback()` itself, which would
mask a regression here. Verified by inverting the check and watching this smoke go red.

Then it builds a `PragmaticJsonOptions` with a source-generated `JsonSerializerContext` and the fallback
disabled, and round-trips a payload through both the context's `JsonTypeInfo` and the seam's resolver
chain. If any part fell back to reflection, the publish would raise trim/AOT warnings or the run would
throw `NotSupportedException`.

```pwsh
pwsh examples/aot-smoke/publish-and-smoke.ps1
```

Expected output ends with:

```
AOT-SMOKE-OK: fallback off by default; {"orderId":"A-100","quantity":3,"items":["widget","gadget"]}
AOT smoke PASSED.
```

## 2. SG-generated context (`Pragmatic.Aot.GeneratedContext`)

`Pragmatic.Aot.GeneratedContext/Program.cs` opts in **purely via the csproj** (`<PublishAot>` /
`<PragmaticGenerateJsonContext>`, no assembly attribute) so the Pragmatic SG **emits** the
`JsonSerializerContext`. It covers every supported shape and round-trips each with the reflection fallback
disabled — a Native AOT proof that the generated context is complete and correct end-to-end:

- a mutable `[Job]` parameter class (`JobParams`),
- a mutable `[MapFrom]` HTTP DTO (`CustomerDto`),
- an **init-only** `[MapFrom]` DTO (`ProfileDto`) — assigned via `[UnsafeAccessor]` setters,
- a **positional record** `[Job]` parameter (`OrderLineDto`) — constructed via an `[UnsafeAccessor]` ctor,
- a DTO with an **enum** (`ShipmentDto`), asserting it serializes as a *name*, not a number,
- a DTO with a **`required` member** (`TicketDto`) — constructed via an `[UnsafeAccessor]` ctor, because
  `new T()` against required members is a compile error the deserializer is about to make moot. This is
  the shape of every **generated request body**, so without that ctor the context would not compile for
  any of them.

⚠️ The enum case is here because it was missing, and its absence hid a real defect: the generated
context baked in the numeric enum converter, so the same value went out as `"Shipped"` through the
reflection path and `2` through this one — enabling `<PublishAot>` silently changed the wire format of
every enum in an API. The smoke registers `JsonStringEnumConverter` exactly as the generated host entry
point does; without that it would be exercising a configuration no real application runs.

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-generated.ps1
```

Expected output ends with:

```
AOT-GEN-OK: {"name":"widget","count":3,"tags":["a","b"]} | {"name":"acme","age":42} | {"sku":"SKU-1","quantity":5} | {"reference":"SH-9","status":"Shipped"} | {"code":"T-77","note":"urgent"}
AOT generated-context smoke PASSED.
```

## 3. HTTP under AOT (`Pragmatic.Aot.WebEndpoint`)

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-web.ps1
```

The first two samples prove the *serialization* seam; neither starts a web server. This one is the
HTTP path: a generated endpoint, published Native AOT, calling itself. It runs twice — with the
reflection fallback disabled (the real AOT configuration) and with it on — and both must answer
correctly.

### What it caught

When it was written, it failed. On the plainest possible endpoint — one `[DomainAction]`, two body
properties, a DTO back — a published AOT binary answered **500** with the fallback off, and **200
with body `{}`** with it on. Under the JIT the same binary answered `201 {"title":"a story","slots":3}`.
Nothing else in the repo would have told you the two differ.

The cause was not the JSON context. The generator emitted `endpoints.MapPost(route, async (…) => …)`,
and ASP.NET's AOT story for minimal APIs is the **Request Delegate Generator** — itself a source
generator, which therefore never sees another generator's output. The calls fell through to the
runtime `RequestDelegateFactory`, which under AOT mis-handled the delegate: it treated the
`Task<IResult>` return as a value to serialize rather than a result to execute.

Two probes settled the design and were then removed:

```
probe /probe/factory:  200 {}                                   ← RequestDelegateFactory, minimal signature
probe /probe/delegate: 200 {"title":"probe","slots":7}           ← RequestDelegate
probe /probe/filtered: 200 {"title":"handler","slots":0} filter=yes
```

RDF is unusable under AOT even with a single `HttpContext` parameter and no binding, so there was no
"simple enough" signature to keep. `RequestDelegate` works, and — the finding that made the rewrite
affordable — **endpoint filters still run on it**. Generated endpoints are now mapped that way and
bind their own parameters.

⚠️ One non-AOT-safe `Map` call poisons the **whole** routing table, not just its own route:
`EndpointRoutingMiddleware` builds every endpoint at once. There is no partial migration.

## Requirements

The native toolchain for the target RID:

- **Windows**: a Visual Studio C++ workload. The scripts put the installer directory on `PATH`
  themselves when `vswhere.exe` is not already resolvable, so a plain shell works; without the
  workload installed at all, the link step still cannot run.
- **Linux**: `clang` + `zlib` dev packages. **macOS**: Xcode command-line tools.

Verified on `win-x64` (VS 18, ILC 10.0.5): samples 1 and 2 publish with **0 IL2026/IL3050
warnings** and exit 0, including the `required`-member case. The generated-context binary is ~3.8 MB
and prints `AOT-GEN-OK: … | {"name":"acme","age":42}` (job params + mapping DTO). Sample 3
publishes with **0 IL2026/IL3050 warnings** and passes in both modes.

## What "AOT" currently means here

| Claim | State |
|---|---|
| Messages, events, job parameters, sagas, mapping DTOs serialize AOT-safely | ✅ proven, sample 2 |
| Generated **request bodies** are covered by the context | ✅ proven, sample 2 shape + Showcase 16/16 |
| **Response types** are covered by the context | ✅ covered; proven serializable in sample 3's own output |
| A generated **HTTP endpoint** works under AOT | ✅ proven, sample 3, both modes, 0 IL warnings |
| Binding failures answer 400 **with a body** | ✅ ProblemDetails, covered by its own context |
| Remote boundary invokers are AOT-safe | ✅ typed `JsonTypeInfo`; the envelope is a named record |
| Patch DTO converters, saga orchestrators | ✅ typed `JsonTypeInfo` |
| String enums resolve without runtime codegen | ✅ generic converter, no `MakeGenericType` |
| The JSON reflection fallback is **off by default** under AOT | ✅ proven, sample 1 — and the check is not vacuous |
| Multi-error `Result<T, E1, …>` serializes AOT-safely | ✅ generated converter from `[assembly: JsonResultContract<…>]` |
| Error schemas and discriminators reach OpenAPI without reflection | ✅ generated module initializers; the reflective enricher is gone |
| The **generated client SDK** is AOT-safe | ⚠️ it carries a JSON context, not yet proven by a smoke (see below) |
| EF Core | out of scope, stated |

Five reflective call sites remain outside EF, and all five are already unreachable or fail-loud under
AOT: the JSON fallback (off by default, proven above), the template accessor and Temporal's resolver
(both throw rather than resolve reflectively), and `HostTopologyInfo.FromAssembly`, which reads a DLL
and is opt-in. They stay in the source because deleting them would remove behaviour a JIT app relies
on, not because AOT needs them gone. The runtime's reflective fallbacks are listed in
[`docs/CONVENTIONS.md`](../../docs/CONVENTIONS.md#reflection).

### Still open: proving the generated client SDK

`Pragmatic.Client.SourceGenerator` emits DTOs, an `HttpClient`, and a `PragmaticJsonContext` whose
`JsonTypeInfo` are written out longhand from the API manifest (`ClientJsonContextBuilder`) — a
`[JsonSerializable]` context cannot work, because System.Text.Json's own generator never sees our
output. The generated client serializes through that context. What is missing is the proof: no smoke
here publishes a client Native AOT, so "AOT-safe" is designed, not measured.
