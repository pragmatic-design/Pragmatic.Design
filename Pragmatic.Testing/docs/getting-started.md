# Getting Started

Pragmatic.Testing turns the contracts your app already declares — endpoints, permissions, entities,
state transitions — into executable tests, and gives you a typed client to write the rest by hand.
You wire it once per test project; from then on new endpoints arrive with their contract tests already
written.

## 1. Reference the pieces

The test project references the app under test, the runtime harness, and the generator:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\MyApp.Host\MyApp.Host.csproj" />
  <ProjectReference Include="...\Pragmatic.Testing\src\Pragmatic.Testing\Pragmatic.Testing.csproj" />
  <ProjectReference Include="...\Pragmatic.Testing\src\Pragmatic.Testing.SourceGenerator\Pragmatic.Testing.SourceGenerator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The generator scans the compilation — the test assembly and every referenced module — for
`[Endpoint]` types. Endpoints still marked `[assembly: PendingContract]` (a body that is
`throw Behavior.Pending()`) are skipped, and their tests appear on their own once implemented.

## 2. Supply the application

Every generated test class joins the `PragmaticContractTests` xUnit collection and reads its
`HttpClient` from `PragmaticContractHost`. You provide that client once, in a collection fixture that
boots the app:

```csharp
public sealed class ContractAppFixture : IAsyncLifetime
{
    private readonly PostgresFixture _db = new();
    private MyAppWebFactory? _factory;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _factory = new MyAppWebFactory(_db);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "contract-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "contract-user");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");

        PragmaticContractHost.Client = client;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(PragmaticContractHost.Collection)]
public sealed class ContractTestCollection : ICollectionFixture<ContractAppFixture>;
```

A broad default grant on this client is fine and usually necessary: the positive contracts need to
reach their endpoints. The negative contracts do **not** inherit it — they set an explicit empty
permission header on their own request, which overrides the client default for that call.

## 3. Build

That is the whole setup. Build the test project and the contract tests exist:

```
_ContractTests.Booking.Auth.g.cs        authorization contracts
_ContractTests.Booking.Crud.g.cs        create / validation / tenant isolation
_ContractTests.Booking.Transitions.g.cs state-machine contracts
_Api.Booking.g.cs                       typed client for hand-written tests
```

Run them like any other xUnit tests. See [Contract tests](contract-tests.md) for what each family
asserts, and [Typed test client](typed-client.md) for writing your own.

## 4. Write your own tests with the typed client

```csharp
using Pragmatic.Testing;
using Pragmatic.Tests.Generated;

var created = await Api.Booking.CreateGuestAsync(Client, new
{
    firstName = "Ada", lastName = "Lovelace", email = "ada@example.com"
});
created.Raw.ShouldBeCreated();

var fetched = await Api.Guests.GetGuestAsync(Client, guestId);
var guest = await fetched.ReadAsync();
guest.Email.Should().Be("ada@example.com");
```

Routes and verbs are resolved at compile time from the app's `ApiRoutes`, so renaming a route breaks
the test at build time rather than at run time.

## Acting as a specific user

`PragmaticTestIdentity` sets the dev-identity headers the host trusts when bearer auth is off:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices/" + id);
request.AsUser("auditor-1", tenantId: "acme", userName: "Auditor", "billing.invoice.read");
var response = await Client.SendAsync(request);
```

Applied to a request, the permission header is always written — empty when you pass no permission —
so an underprivileged caller stays underprivileged even when the shared client carries a wildcard
grant. The tenant and user-name headers behave the opposite way: omit them and the client defaults
apply.
