# Pragmatic AOT smoke

Native AOT checks for the serialization seam, the HTTP path, logging, persistence, a composed host, the message
bus and the image codecs. Eleven samples, each published Native AOT and run by the gate (`node scripts/check.mjs --tier aot`).
The first four are described here; the persistence ones in [5–7](#5-7-persistence-on-the-engines), the composed host in
[8](#8-one-composed-host-pragmaticaothost), the message bus in [9](#9-the-in-memory-message-bus-pragmaticaotmessaging), the composed
host on the documents engine in [10](#10-the-composed-host-on-the-documents-engine-pragmaticaothostdocuments), the image codecs
in [11](#11-the-managed-image-codecs-pragmaticaotcodecs).

**Where they run:** in CI, the `Native AOT smokes` job of `ci.yml` on `ubuntu-latest`, on every pull request and merge
group whose scope is `full` and on every push to `main`. A release runs `ci.yml` before `pack` (`release.yml`), so a
`nuget-v*` tag does not publish with a red smoke (#825). On a developer machine, `--tier aot` (Windows or Linux).

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
every enum in an API. The smoke registers `PragmaticStringEnumConverter` exactly as the generated host
entry point does (it writes the names `JsonStringEnumConverter` writes, and needs no dynamic code to
create); without that it would be exercising a configuration no real application runs.

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

## 4. Declared redaction under AOT (`Pragmatic.Aot.Logging`)

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-logging.ps1
```

Logs two values through `PragmaticJsonProvider` with the generated redaction map, published Native AOT:

- a `Customer` whose `[PersonalData]` e-mail must come out masked, with the reference kept;
- a `Payment` whose `[PersonalData]` IBAN sits beside an `object` member. The generator cannot describe
  that type, so there is no JSON metadata for it and nothing else may reflect over it. The entry must
  still be written, with the whole value masked, and counted in `DeclaredRedactor.ValuesWithoutMetadata`.

### What it caught

When it was written, it failed: **nothing was written at all**. The redactor serialized the value with
`JsonSerializer.Serialize(value, null)`, reflection-based, and `PublishAot` turns that off even under the
JIT. The exception landed in the provider's catch, and only `LastError` said why. Every entry carrying a
declared type was lost.

The redacted types are now roots of the generated JSON context, and the generated map hands that context
to the redactor (`IRedactionMap.TypeInfoResolver`). A redacted value is written in camelCase, like every
other complex value the JSON providers write, on the JIT and under AOT alike.

## 5-7. Persistence on the engines

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-sql.ps1
pwsh examples/aot-smoke/publish-and-smoke-documents.ps1
pwsh examples/aot-smoke/publish-and-smoke-boundaries.ps1
```

| Sample | What it runs, published Native AOT |
|---|---|
| `Pragmatic.Aot.SqlEngine` | The Sql engine on SQLite: queries with the caller's filters, a join to a row the caller cannot see, `WithoutFilters`, `Include`, a declared query, a grid load, a temporal history, a computed scope, a trait's child, a tree, and an `ExecuteUpdate` on the caller's rows |
| `Pragmatic.Aot.DocumentsMongo` | The documents engine on MongoDB (started in Docker) |
| `Pragmatic.Aot.Boundaries` | Two boundaries on two engines in one process (Sql on SQLite, documents on MongoDB): the schema applied at start, a write through each, the owner each save stamps, a unit of work per boundary, and another caller's rows hidden |

Each prints one line per check and exits with the number that failed. They run through the boundaries'
generated registration and repositories, not through an HTTP host: that composition is sample 8.

## 8. One composed host (`Pragmatic.Aot.Host`)

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-host.ps1
```

An application's shape, published Native AOT:
- **The module** (`Pragmatic.Aot.Host.Notes`): one entity on the Sql engine, a create mutation and a read query, both exposed with
  `[Endpoint]` and each requiring its permission.
- **The host:** includes the module on a SQLite database, and starts through the generated entry point (`PragmaticApp.RunAsync`).
  - Pragmatic Migrations applies the module's schema at start.
  - The caller comes from the development identity (`X-User-*` headers), with the permissions it carries trusted.
- **The checks:** a hosted service inside the same process calls the host once it has started, then stops it. The exit code is the
  number of checks that failed.

  | Check | Answer |
  |---|---|
  | `POST /api/notes` with the permission | `201` and the note |
  | the note's enum, sent and returned | by its name, `"kind":"Final"` |
  | `GET /api/notes/{id}` | `200`, the same values |
  | no identity | `401` |
  | a caller without the permission | `403` |

The five checks pass under Native AOT, and **the publish is warning-free**: the script's budget of trim/AOT warnings is **0**, and
it fails on any.

The budget started at 14, and each warning went with the issue that removed its cause:

| Warnings | From | Issue |
|---|---|---|
| 4 | The generated entry point: telemetry options read by the reflecting configuration binder, enums registered through `JsonStringEnumConverter`. Now typed code, and `PragmaticStringEnumConverter` | #857 |
| 4 | EF Core's OpenTelemetry instrumentation, registered on every host. Now by `Pragmatic.Persistence.EFCore`, which this host on the Sql engine does not reference | #858 |
| 6 | C#'s `dynamic` binder, rooted by the domain event dispatcher's fallback for an event no generated table knows. Now behind `RuntimeFeature.IsDynamicCodeSupported`, which Native AOT folds to `false` | #859 |

## 9. The in-memory message bus (`Pragmatic.Aot.Messaging`)

```bash
pwsh examples/aot-smoke/publish-and-smoke-messaging.ps1
```

A console application with one `[MessageHandler]` and the registration the generator writes for it. It dispatches a message untyped
through `IMessageBus.DispatchAsync`, the path that probes the generated dispatch tables, and checks that the handler received it.

The script counts the publish's trim/AOT warnings against a budget of **0**. The bus's fallback for a message no generated table knows
was `PublishAsync((dynamic)message, …)`, unguarded: it compiled C#'s runtime binder into every application that used the bus, with the
six warnings the composed host had for the same reason in the event dispatcher. It is now behind `RuntimeFeature.IsDynamicCodeSupported`
(#869).

## 10. The composed host on the documents engine (`Pragmatic.Aot.HostDocuments`)

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-host-documents.ps1
```

Sample 8 on MongoDB (in Docker, started by the script as sample 6 does):
- **The module** (`Pragmatic.Aot.HostDocuments.Notes`): sample 8's, compiled from the same files, with its boundary on the documents
  engine. That boundary is the one file of its own, and an index on the note's kind (`Note.Index.cs`).
- **The host:** includes the module on its database, which the topology names as on the Showcase's documents host: the connection string
  is MongoDB's, the database named in its path. Pragmatic Migrations, with no SQL driver referenced, has the generated start apply the
  boundary's documents schema.
- **The checks:** sample 8's five, from the same `SmokeClient`, and a sixth once the host has stopped: a second `EnsureAsync` of the
  boundary's schema finds it unchanged. A write creates the collection but not its index, so only the host's start can have applied it;
  without `UsePragmaticMigrations` that check fails, on both targets.

The publish is warning-free, against a budget of **0**, as sample 8's (#980).

## 11. The managed image codecs (`Pragmatic.Aot.Codecs`)

```pwsh
pwsh examples/aot-smoke/publish-and-smoke-codecs.ps1
```

A server's upload path on `Pragmatic.Imaging.Codecs`, published Native AOT with IL2026/IL3050 as errors: a 2048 × 1536 image
encoded as JPEG, identified, decoded at the scale that covers a 400 × 300 thumbnail, resized to it, encoded as PNG and read back
pixel for pixel, and a QR code. Each check prints `ok` or `FAIL`; the last line is `AOT-CODECS-OK` only when all pass.

On x64 it also checks `Avx2.IsSupported`. Native AOT resolves it at compile time, and compiles for x86-64-v2 unless told
otherwise, so on that default every AVX2 path of the codecs drops out of the binary. The package ships a `buildTransitive` targets
file that sets x86-64-v3; a project reference does not carry it, so the smoke imports the file as a package consumer would get it.
Without the import ILC is run with no `--instruction-set` and the check fails; with it, `--instruction-set:x86-64-v3` and the check
passes (#983).

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
| Declared redaction masks under AOT, and a type without metadata is masked whole, not lost | ✅ proven, sample 4 |
| The JSON reflection fallback is **off by default** under AOT | ✅ proven, sample 1 — and the check is not vacuous |
| Multi-error `Result<T, E1, …>` serializes AOT-safely | ✅ generated converter from `[assembly: JsonResultContract<…>]` |
| Error schemas and discriminators reach OpenAPI without reflection | ✅ generated module initializers; the reflective enricher is gone |
| Persistence on the **Sql engine** works under AOT | ✅ proven, sample 5 |
| Persistence on the **documents engine** works under AOT | ✅ proven, sample 6 |
| **Two boundaries on two engines** in one process | ✅ proven, sample 7 |
| **One composed host** (endpoints, writes through an engine, migrations) works under AOT | ✅ proven, sample 8: 201, 200, 401 and 403 from the native binary, an enum by its name, and a publish with 0 trim/AOT warnings (#857, #858, #859) |
| The same **composed host on the documents engine** works under AOT | ✅ proven, sample 10: sample 8's five checks over MongoDB, the documents schema applied at start, and a publish with 0 trim/AOT warnings (#980) |
| The **in-memory message bus** dispatches to a generated handler under AOT | ✅ proven, sample 9, with 0 trim/AOT warnings (#869) |
| The **managed image codecs** run under AOT with their vector paths alive | ✅ proven, sample 11: JPEG, PNG, resize and QR, with 0 trim/AOT warnings and AVX2 on (#983) |
| The **generated client SDK** is AOT-safe | ⚠️ it carries a JSON context, not yet proven by a smoke (see below) |
| EF Core | out of scope, stated: the JIT option |

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
