---
name: pragmatic-use-testing
description: Use when writing or wiring tests for a Pragmatic app, mocking a boundary or IClock, or when a generated contract test fails or is missing (Pragmatic.Testing, contract tests, WebApplicationFactory, Testcontainers).
---

# Pragmatic Use Testing

**Covers:** Test a Pragmatic app with Pragmatic.Testing: generated contract tests (authorization, CRUD, tenant isolation, transitions), a typed Api client, the real host under WebApplicationFactory with Testcontainers, test identities, assertions, generated mocks, clock/bus/mail/log harnesses.

A Pragmatic application already declares what it promises: endpoints, permissions, entities, state
machines. `Pragmatic.Testing` turns those declarations into tests, and the modules ship harnesses for
the parts a test has to control (time, the bus, mail). Write by hand only what a declaration cannot
say: the business rule.

## Three kinds of test, three projects

| Project | What it tests | Runs |
|---|---|---|
| `{App}.Tests` (unit) | Domain rules, handlers, services, with generated mocks | milliseconds, no container |
| `{App}.IntegrationTests` | The real host over HTTP against a real database; generated contract tests live here | Testcontainers (Docker) |
| optional restart suite | Data that survives a process restart, migrations on an existing database | the built host as its own process; see `../pragmatic-ecosystem/references/cookbook/restart-and-persistence-tests.md` |

Keep them apart: a unit suite that needs Docker stops being run.

## Packages

```xml
<!-- integration test project -->
<ProjectReference Include="..\..\src\MyApp.Host\MyApp.Host.csproj" />
<PackageReference Include="Pragmatic.Testing" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Testing.SourceGenerator" Version="1.0.0-alpha.1" PrivateAssets="all" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
<PackageReference Include="Testcontainers.PostgreSql" />

<!-- unit test project: mocks and comparers are separate generators, opt in to each -->
<PackageReference Include="Pragmatic.Testing" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Testing.Mocking.SourceGenerator" Version="1.0.0-alpha.1" PrivateAssets="all" />
<PackageReference Include="Pragmatic.Testing.Comparers.SourceGenerator" Version="1.0.0-alpha.1" PrivateAssets="all" />
```

Module harnesses, only where used: `Pragmatic.Temporal.Testing` (`TestClock`),
`Pragmatic.Messaging.Testing`, `Pragmatic.Email.Testing`, `Pragmatic.Notifications.Testing`.
`TestI18N` is in `Pragmatic.Internationalization` itself.

## The host under test

The generated host is the application: test **it**, not a host rebuilt for tests. Override only what
the test must own (the connection string, a clock, a transport):

```csharp
public sealed class AppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");                       // dev identity headers are trusted here
        builder.UseSetting("ConnectionStrings:App", connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.AddEmailTestHarness();
        });
    }

    public TestClock Clock { get; } = TestClock.AtNoon(2026, 5, 6);
}
```

⚠️ **A setting that decides which services get registered (an identity scheme, a provider, a
transport) goes through `UseSetting`, not `ConfigureAppConfiguration`.** The host's startup callback
reads configuration before `ConfigureAppConfiguration` values exist; a lambda that runs later (a
connection string read inside `AddDbContext`) sees both. With the wrong channel the host silently keeps
its default and the suite can stay green for the wrong reason.

One PostgreSQL container per test collection (`ICollectionFixture`), the schema applied once; each
test creates its own rows with unique keys rather than resetting the database.

## Who the caller is

In Development the host trusts dev-identity headers. Set them **on the request**:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{id}");
request.AsUser("auditor-1", tenantId: "acme", userName: "Auditor", "billing.invoice.read");
var response = await client.SendAsync(request);
```

⚠️ `AsUser` on a request always writes the permission header (empty when you pass none), so a caller
with no permission stays without one even when the shared client carries `X-User-Permissions: *`. A
denial test on the shared client passes nothing and proves nothing.

⚠️ When a test switches to a real scheme (JWT), turn the dev headers **off** and send only the token:
with both on, the suite is green whichever of the two works.

## Generated contract tests

With `Pragmatic.Testing.SourceGenerator` referenced, every `[Endpoint]` the app exposes arrives with
its tests; you write one fixture:

```csharp
public sealed class ContractAppFixture : IAsyncLifetime
{
    private readonly PostgresFixture _db = new();
    private AppFactory? _factory;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _factory = new AppFactory(_db.ConnectionString);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "contract-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "contract-user");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        PragmaticContractHost.Client = client;

        // A create the generator cannot fill (a foreign key, a nested collection): say what to post.
        PragmaticContractHost.BodyFor = operation => operation == "CreateDraftInvoiceMutation"
            ? new { customerId = _db.SeededCustomerId, lines = new[] { new { description = "x", amount = 10 } } }
            : null;
    }

    public async Task DisposeAsync() { _factory?.Dispose(); await _db.DisposeAsync(); }
}

[CollectionDefinition(PragmaticContractHost.Collection)]
public sealed class ContractTestCollection : ICollectionFixture<ContractAppFixture>;
```

What you get, per boundary: **authorization** (without the permission → rejected, with it → not
forbidden), **CRUD** (create, validation, tenant isolation), **state transitions** (legal → 2xx,
illegal → 409, for operations declaring `[TransitionsTo<TState>]`), and a typed client.

- An operation with **no `[RequirePermission]`** gets no authorization contract: nothing refuses it.
  Read `ContractCoverage.Operations` and pin `ContractCoverage.Uncovered` in a test of your own: the
  generated suite reports what it wrote, never what it skipped.
- An endpoint whose body is still `throw Behavior.Pending()` is skipped until implemented.
- Several services: `PragmaticContractHost.UseClientFor("Intake", intakeClient)` per boundary; a
  boundary with no client is then an error, not a fallback.
- Each transition gets **both** answers: the one its initial state gives, and the opposite one from a
  state reached by walking the entity's other declared transitions (`PRAG2363` when none reaches it). An
  entity nothing creates over HTTP (raised by an event, written by a message handler) is brought into
  its initial state by `PragmaticContractHost.ArrangeFor = async (entity, client) => …` returning its id;
  without it the contract fails naming what to set. Nothing is skipped.
- `ShouldBeRejected()` accepts any 4xx; assert `ShouldHaveStatus(HttpStatusCode.Forbidden)` when the
  claim is specifically 403.

## Hand-written tests: the typed client

```csharp
using Pragmatic.Testing;
using Pragmatic.Tests.Generated;

var created = await Api.Booking.CreateGuestAsync(client, new { firstName = "Ada", lastName = "Lovelace" });
created.Raw.ShouldBeCreated();

var guest = await (await Api.Guests.GetGuestAsync(client, id)).ReadAsync();   // ApiResponse<GuestDto> → GuestDto
guest.LastName.Should().Be("Lovelace");
```

Routes and verbs come from the app's generated `ApiRoutes`: renaming a route breaks the test at build.
Assert on the response the way a client would see it (`ShouldBeOk()`, `ShouldBeCreated()`,
`ShouldBeNoContent()`, `ShouldBeBadRequest()`, `ShouldBeUnprocessable()`, `ShouldBeNotFound()`,
`ShouldBeConflict()`, `ShouldBeForbidden()`, `ShouldHaveStatus(code)`) and read the body.
`ShouldNotBeForbidden()` also passes a 500; `ShouldIdentifyTheCreatedAsync()` reads the id a 201
points at.

## Assertions

`Pragmatic.Testing.Assertions` is the framework's own `.Should()` library, with no FluentAssertions:
`Be`, `BeEquivalentTo`, `Contain`, `HaveCount`, `BeEmpty`, `Throw<T>()` / `ThrowAsync<T>()`,
`BeCloseTo`, ordering, type checks. For a DTO without value equality declare a comparer, so a
failure names the member that differs:

```csharp
[assembly: GenerateComparer<InvoiceDto>]
// dto.Should().BeEquivalentTo(expected) → "Expected invoice.Total to be 42, but found 43"
```

## Mocks, generated

```csharp
[assembly: GenerateMock<IClock>]
[assembly: GenerateMock<IBookingReservationsActions>]

var reservations = new BookingReservationsActionsMock();
reservations.Confirm.Returns(confirmed);                             // a Task<T>/ValueTask<T> member: Returns(value) wraps it
reservations.Confirm.When(Arg.Is<Guid>(id => id == known)).Returns(confirmed);   // unspecified args match anything
// … exercise the system under test …
reservations.Confirm.Received(1, Arg.Is<Guid>(id => id == known), Arg.Any<CancellationToken>());
reservations.Cancel.DidNotReceive();
```

- Declared per test assembly; the class is the interface name without `I` plus `Mock`
  (`IVoidDomainActionInvoker<X>` → `VoidDomainActionInvokerOfXMock`), `Name =` overrides it.
- Each member is a configurable property: `Returns`, `Throws`, `When(...)`, `Received`,
  `DidNotReceive`, `ClearReceivedCalls`; void methods have `Does(callback)`; properties `Returns`/`Set`.
- ⚠️ **Overloads get the parameter count appended** (`MarkPaymentReceived2`), a remaining tie `_2`;
  generic methods share one member.
- `Arg.Any<T>()`, `Arg.Is(value)`, `Arg.Is<T>(predicate)`, `Arg.Do<T>(capture)`.
- Mock the **boundary interface** (`I{Boundary}Actions`), not the invoker: it is what a handler in
  another module depends on.

## Time, bus, mail, notifications, logs, culture

| Harness | Setup | Assert |
|---|---|---|
| `TestClock` (`Pragmatic.Temporal.Testing`) | `TestClock.AtNoon(2026, 5, 6)`, `new TestClock(instant)`; register as `IClock` | `Advance(...)`, `AdvanceDays(n)`, `SetBeforeRomeSpringForward()` for DST |
| Messaging | `services.AddMessagingTestHarness()` records only; `AddDispatchingTestHarness()` also runs the handlers | `HasPublished<T>(predicate)`, `PublishedOf<T>()`, `ConsumedOf<T>()`, `FaultedOf<T>()`, `Reset()` |
| Mail | `services.AddEmailTestHarness()` → `InMemoryTransport` | `HasSentTo(address)`, `Sent`, `SentWhere(predicate)` |
| Notifications | `services.AddNotificationTestHarness()` | `Sent`, `SentWhere(predicate)` |
| Logs | `var logs = new CapturedLogs(); builder.ConfigureLogging(l => l.AddProvider(logs));` | `logs.PropertyOf("Message", "Property")`: the structured property, not the text; `Clear()` between tests on a shared fixture |
| Culture | `TestI18N.WithCultureAsync(CultureCode.Italian, async () => …)` | the translated/formatted value |

⚠️ Drive a background job or a scheduled pass through the unit it calls, with the instant as an
argument, not through the scheduler: going through the scheduler also asserts its polling interval and
lease, and the test becomes a timing test.

⚠️ A test process whose default culture is not the one you assert on will pass on one machine and fail
on another; set the culture explicitly (headers, `TestI18N`), never rely on the machine's.

## Rules that keep a suite honest

- **A denial needs its control.** "Refused without the permission" is satisfied by refusing everybody;
  pair it with "accepted with it" on the same route.
- **A test green on the first run is measured by removing the thing it guards.** Delete the
  attribute, the registration, the rule: exactly the tests that should go red must go red.
- **No `Skip` without a reason that names what is missing**, and a skipped test is unknown, not green.
- **Assert the effect, not the call**: the row in the database, the message on the bus, the line in
  the log with its property, not that a method ran.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing and Showcase example
applications (test code that compiles and runs as part of `Invoicing.IntegrationTests` and
`Showcase.Tests`) and kept identical to it by the gate: the host under test, the database
fixture, the fixture of the generated contract tests and the test that pins what they emit, a
hand-written test with the clock and the mailbox, and the generated mocks in a unit test.

`TestI18N` and the messaging harness are used by no tested application yet, so there is no example of
them here: the sections above are the reference.
