---
title: "Pragmatic.Testing"
description: "Compile-time testing infrastructure for Pragmatic APIs: generated contract tests, a typed"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Testing/README.md
sidebar:
  order: 0
  label: Overview
---
Compile-time testing infrastructure for Pragmatic APIs: generated contract tests, a typed
test client (`Api.*`), and HTTP assertion helpers. Zero reflection — the source generator
reads the endpoint contracts your app already emits.

## What you get

| Piece | Where it lives | Purpose |
|---|---|---|
| Contract tests | generated into the test project | Authorization, CRUD and state-transition suites per endpoint — see [Contract tests](/modules/testing/contract-tests/) |
| `PragmaticTestIdentity` | `Pragmatic.Testing` runtime | Dev-identity headers (`AsUser`) for acting as a specific user, tenant, or a caller with no permission at all |
| Typed client `Api.*` | generated into the test project | `Api.{Boundary}.{Name}Async(client, ...)` → `ApiResponse` / `ApiResponse<T>`; routes and verbs resolved at compile time |
| `ApiResponse` / `ApiResponse<T>` | `Pragmatic.Testing` runtime | Lazy deserialization (`ReadAsync`), chainable assertions, implicit conversion to `HttpResponseMessage` |
| `PragmaticHttpAssertions` | `Pragmatic.Testing` runtime | `ShouldBeOk()`, `ShouldBeCreated()`, `ShouldBeUnprocessable()`, `ShouldBeConflict()`, `ShouldHaveStatus(code)` … framework-agnostic (BCL-only) |
| `PragmaticJson.Options` | `Pragmatic.Testing` runtime | The host's JSON conventions, for hand-rolled serialization in tests |
| `.Should()` assertions | `Pragmatic.Testing` runtime (`Assertions/`) | The assertion library the repository's own tests use — no FluentAssertions |
| Mocks | `Pragmatic.Testing.Mocking.SourceGenerator` | `[GenerateMock<T>]` → a reflection-free, AOT-safe mock; opt in by referencing the generator |
| Comparers | `Pragmatic.Testing.Comparers.SourceGenerator` | `[GenerateComparer<T>]` → a member-by-member `BeEquivalentTo` for types without value equality |

The client pairs with the `ApiRoutes` class the unified source generator emits into the app
assembly (route constants + typed URL builders) — rename a route and every test that calls
it breaks at build, not at runtime.

## Quick Start

The test project references the app plus both Testing pieces:

```xml
<ProjectReference Include="..\..\src\MyApp\MyApp.csproj" />
<PackageReference Include="Pragmatic.Testing" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.Testing.SourceGenerator" Version="1.0.0-alpha.*" PrivateAssets="all" />
```

(Inside this repository the same two are `ProjectReference`s, the generator with
`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.)

```csharp
using Pragmatic.Testing;           // ApiResponse, assertions, PragmaticJson
using Pragmatic.Tests.Generated;   // generated Api client

var created = await Api.Booking.CreateGuestAsync(Client, new
{
    firstName = "Ada", lastName = "Lovelace", email = "ada@example.com"
});
created.Raw.ShouldBeCreated();

var fetched = await Api.Guests.GetGuestAsync(Client, guestId);
var dto = await fetched.ReadAsync();   // ApiResponse<GuestDto> → GuestDto
```

Generated contract test classes join the `PragmaticContractTests` xUnit collection;
`PragmaticContractTestBase` exposes the shared `Client` wired once by the collection
fixture — no per-class setup.

## Status

**Functional** within 1.0.0-alpha — generated contract tests, the typed test client (`Api.*`), and the
HTTP assertions; four of the reference applications' suites run on it. See the
[roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Getting started](/modules/testing/getting-started/) | Wiring the generator, the collection fixture, your first test |
| [Contract tests](/modules/testing/contract-tests/) | What each generated family asserts — and what it deliberately does not |
| [Typed test client](/modules/testing/typed-client/) | The two tiers (`ApiRoutes` + `Api`), setup, usage, limits |
| [Common mistakes](/modules/testing/common-mistakes/) | Denial tests that pass for the wrong reason, assertion strength, fixture wiring |
| [Troubleshooting](/modules/testing/troubleshooting/) | Nothing generated, missing client, unexpected 401/400 |

## Requirements

- .NET 10.0+
- xUnit (the generated contract tests are xUnit classes)

## License

Part of the [Pragmatic.Design](/modules/testing/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Testing is **MIT-licensed**.
