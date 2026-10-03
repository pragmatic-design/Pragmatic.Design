# Showcase Walkthrough

> Step-by-step guide to understanding, running, and extending the Pragmatic.Design Showcase.

## 1. What is Showcase?

Showcase is a full-stack reference application that demonstrates every module in the Pragmatic.Design ecosystem. It models an **international hotel booking platform** with three bounded contexts:

- **Catalog** -- hotel properties, room types, amenities, cancellation policies.
- **Booking** -- reservations, guests, room assignments, check-in/check-out workflows.
- **Billing** -- invoices, payments, line items, cross-boundary financial processing.

The domain is intentionally rich enough to exercise the entire Pragmatic.Design feature set: source-generated actions, entities with state machines, domain events crossing boundary lines, typed error results, i18n, caching, authorization, jobs, messaging, and more.

All source code lives under:

```
examples/showcase/
```

### 1.1 Why Showcase Exists

Showcase serves three purposes:

1. **Validation** -- every new Pragmatic feature must have at least one Showcase integration demonstrating it works end-to-end against a real database.
2. **Documentation by example** -- developers learn Pragmatic patterns by reading Showcase source.
3. **Regression detection** -- 200+ Showcase tests and 153+ integration tests guard against cross-module breakage.

---

## 2. Architecture

### 2.1 Project Layout

```
examples/showcase/
├── src/
│   ├── Showcase.Catalog/         # Catalog boundary (class library)
│   ├── Showcase.Booking/         # Booking boundary (class library)
│   ├── Showcase.Billing/         # Billing boundary (class library)
│   ├── Showcase.Accounts/        # Identity/local auth (class library)
│   ├── Showcase.Host/            # ASP.NET Core host (monolith topology)
│   ├── Showcase.Billing.Host/    # Billing standalone host (distributed topology)
│   └── Showcase.Host.Distributed/# Distributed host (Billing as RemoteBoundary)
├── tests/
│   ├── Showcase.Tests/           # Unit tests (mapping, validation, entities, specs)
│   └── Showcase.IntegrationTests/# E2E tests against PostgreSQL via Testcontainers
├── docker-compose.yml            # PostgreSQL 17 for persistent mode
├── docker-compose.messaging.yml  # RabbitMQ for messaging transport
├── ENV.md                        # Dev environment reference
└── Showcase.slnx                 # Solution file
```

### 2.2 Database Topology

Two databases demonstrate compliance separation:

| Database | Boundaries | Purpose |
|----------|-----------|---------|
| `ShowcaseAppDatabase` | Catalog + Booking | Operational -- shared for SQL joins |
| `ShowcaseFinancialDatabase` | Billing | Financial -- isolated for audit/compliance |

Billing receives data exclusively via domain events (`ReservationConfirmed` triggers `CreateDraftInvoiceMutation`), never via SQL join. This models real-world regulatory separation.

### 2.3 Two Deployment Topologies

**Monolith** (`Showcase.Host`): All three boundaries run in a single process. This is the default and what integration tests target.

**Distributed** (`Showcase.Host.Distributed` + `Showcase.Billing.Host`): Booking and Catalog run in one host. Billing runs as a separate service. Communication uses SG-generated HTTP invokers via `[RemoteBoundary<BillingModule>]`.

```csharp
// Showcase.Host.Distributed/ShowcaseDistributedModule.cs
[Module]
[Include<BookingModule, ShowcaseAppDatabase>]
[Include<CatalogModule, ShowcaseAppDatabase>]
[RemoteBoundary<BillingModule>]       // Billing calls go over HTTP
public sealed class ShowcaseDistributedModule;
```

The SG auto-generates `HttpInvoker` classes for each Billing action (e.g., `CreateInvoiceWithFeesActionHttpInvoker`) that serialize the action, POST to the remote host, and deserialize the result.

---

## 3. How to Run

### 3.1 InMemory Mode (Zero Setup)

```bash
dotnet run --project examples/showcase/src/Showcase.Host
```

Open http://localhost:5000/scalar for the Scalar API UI.

This mode uses EF Core InMemory provider. Demo data (3 hotels, 2 guests) is seeded automatically at startup.

### 3.2 PostgreSQL Mode

Start the database:

```bash
docker compose -f examples/showcase/docker-compose.yml up -d
```

This spins up PostgreSQL 17 on port **5433** (not the default 5432, to avoid conflicts). The hosts'
`appsettings.json` already point at it. The password is the local container's, set in
`docker-compose.yml`, not a secret and not what a deployment uses:

```json
{
  "ConnectionStrings": {
    "App": "Host=localhost;Port=5433;Database=showcase_app;Username=pragmatic;Password=Pragmatic@2026!",
    "Financial": "Host=localhost;Port=5433;Database=showcase_financial;Username=pragmatic;Password=Pragmatic@2026!"
  }
}
```

Then run with `dotnet run --project examples/showcase/src/Showcase.Host`.

### 3.3 With RabbitMQ (Messaging)

```bash
docker compose -f examples/showcase/docker-compose.messaging.yml up -d
```

RabbitMQ 3.13-management runs on port 5672 (AMQP) / 15672 (management UI). Without RabbitMQ, Showcase falls back to Channel-based in-process transport.

### 3.4 Running Tests

Unit tests (no external dependencies):

```bash
dotnet test examples/showcase/tests/Showcase.Tests/
```

Integration tests (requires Docker for Testcontainers PostgreSQL):

```bash
dotnet test examples/showcase/tests/Showcase.IntegrationTests/
```

---

## 4. Domain Flow

The core business flow from guest registration to payment:

### Step 1: Register a Guest

```http
POST /api/v1/guests
Content-Type: application/json

{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john@example.com",
  "phoneNumber": "+1234567890"
}
```

Returns the guest `Guid`. (Or use a seeded guest -- the app seeds two at startup.)

### Step 2: Browse Properties

```http
GET /api/v1/properties?api-version=1.0
```

Returns hotel properties from the Catalog boundary. Each property has room types with base rates and max occupancy.

### Step 3: Create a Reservation

```http
POST /api/v1/reservations?api-version=1.0
Content-Type: application/json

{
  "request": {
    "guestId": "<guest-id>",
    "propertyId": "<property-id>",
    "roomTypeId": "<room-type-id>",
    "checkIn": "2026-04-15T14:00:00Z",
    "checkOut": "2026-04-18T14:00:00Z",
    "numberOfGuests": 2
  }
}
```

This triggers `CreateReservationAction`, which:
1. Validates input (sync: future date, GreaterThan; async: room availability).
2. Calculates total (nights x base rate).
3. Creates the entity with status `Pending`.
4. Raises `ReservationCreated` domain event.

### Step 4: Confirm the Reservation

```http
POST /api/v1/reservations/{id}/confirm?api-version=1.0
```

The `ConfirmReservationMutation` calls `Reservation.Confirm()`, which:
1. Validates the state machine transition: `Pending -> Confirmed`.
2. Raises `ReservationConfirmed` domain event.

**This is where cross-boundary magic happens.**

The `ReservationConfirmedHandler` in Billing catches the event, calculates tax, and calls `billingActions.CreateDraftInvoice(...)` to auto-create a draft invoice in the financial database. No cross-boundary SQL. No shared entity references.

```csharp
// Showcase.Billing/EventHandlers/ReservationConfirmedHandler.cs
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter)]
public sealed partial class ReservationConfirmedHandler(
    IBillingActions billingActions) : IMessageHandler<ReservationConfirmed>
```

### Step 5: Pay the Invoice

```http
POST /api/v1/invoices/{id}/pay?api-version=1.0
Content-Type: application/json

{ "paymentMethod": "CreditCard", "amount": 450.00 }
```

`MarkInvoicePaidAction` transitions the invoice to Paid and raises `InvoicePaid`.

### Step 6: Check In

```http
POST /api/v1/reservations/{id}/checkin?api-version=1.0
```

`CheckInGuestMutation` validates `Confirmed -> CheckedIn` (or `PaymentReceived -> CheckedIn`) via the state machine, records who checked the guest in, and raises `GuestCheckedIn`.

### State Machine

The `ReservationStatus` enum defines all valid transitions:

```
Pending --> Confirmed --> PaymentReceived --> CheckedIn --> CheckedOut
  |              |              |
  +--> Cancelled +--> Cancelled +--> Cancelled
  |              |
  +--> NoShow    +--> NoShow
```

Defined declaratively with `[TransitionFrom]` attributes on each enum value. The SG generates a `TransitionTo()` method that enforces these transitions at runtime, returning a `VoidResult<IError>` on invalid transitions.

---

## 5. Module Interaction

### 5.1 Cross-Boundary Events

Showcase demonstrates the pattern of crossing boundaries via domain events, not shared database access:

```
[Booking] ReservationConfirmed event
    |
    v (via InMemoryEventDispatcher or MessageBus)
[Billing] ReservationConfirmedHandler
    |
    v (calls IBillingActions.CreateDraftInvoice)
[Billing] Invoice entity created in Financial database
```

Events carry **all required data** in their payload. `ReservationConfirmed` includes `ReservationId`, `GuestId`, `PropertyId`, `TotalAmount`, `Currency`, `CheckIn`, `CheckOut`, and `OccurredAt`. The Billing handler never reads from the Booking database.

### 5.2 Boundary Interfaces

Each boundary declares a `[Boundary]` class. The SG generates typed action interfaces:

```csharp
// Declared:
[Boundary]
public partial class BookingBoundary;

// SG generates:
public interface IBookingActions
{
    IDomainActionInvoker<CreateReservationAction, Guid> CreateReservation { get; }
    IVoidDomainActionInvoker<ConfirmReservationAction> ConfirmReservation { get; }
    // ...
}
```

Modules inject `IBillingActions` or `IBookingActions` to invoke cross-boundary logic without knowing implementation details.

### 5.3 Remote Boundary (Distributed Deployment)

In `Showcase.Host.Distributed`, the `[RemoteBoundary<BillingModule>]` attribute tells the SG to generate HTTP invokers instead of direct DI calls. The generated invoker serializes the action parameters, POSTs to the remote Billing host, and deserializes the result.

Configuration:

```json
{
  "Pragmatic": {
    "RemoteBoundaries": {
      "Billing": {
        "BaseUrl": "https://localhost:5010"
      }
    }
  }
}
```

### 5.4 ReadAccess Declarations

Booking reads Catalog entities (Property, RoomType) via SQL join, which is valid when both boundaries share the same database:

```csharp
[Boundary]
[ReadAccess<Property>]
[ReadAccess<RoomType>]
public partial class BookingBoundary;
```

---

## 6. Key Patterns Demonstrated

### 6.1 Entity Traits

The `Reservation` entity demonstrates trait composition:

```csharp
[Entity]        // PersistenceId, Id property, setters
[Auditable]           // CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
[SoftDelete]          // IsDeleted, DeletedAt
[HasOwner]         // OwnerId, ownership filter
[ConcurrencyAware]    // RowVersion for optimistic concurrency
[HasPresets]          // Seeded preset data
[StateMachine<ReservationStatus>]  // Enforced state transitions
[Relation.ManyToOne<Property>]     // FK relationship
[Relation.ManyToOne<RoomType>]     // FK relationship
[Relation.OneToMany<RoomAssignment>]  // Collection navigation
public partial class Reservation : DomainEventSource, IEntity
```

Each attribute triggers SG code generation. `[Auditable]` generates `IAuditable` properties and auto-populates them. `[SoftDelete]` generates `ISoftDelete` and adds a global query filter. `[StateMachine]` generates the `TransitionTo()` method.

### 6.2 State Machines

```csharp
[FastEnum]
public enum ReservationStatus
{
    [InitialState]
    Pending,

    [TransitionFrom(ReservationStatus.Pending)]
    Confirmed,

    [TransitionFrom(ReservationStatus.Confirmed)]
    PaymentReceived,

    [TransitionFrom(ReservationStatus.Confirmed)]
    [TransitionFrom(ReservationStatus.PaymentReceived)]
    CheckedIn,
    // ...
}
```

`[FastEnum]` generates AOT-safe enum helper methods. `[TransitionFrom]` attributes define the state graph. The entity's `TransitionTo()` returns `VoidResult<IError>` -- invalid transitions produce a typed error, not an exception.

### 6.3 Domain Events

Events are defined as immutable records:

```csharp
public sealed record ReservationConfirmed(
    Guid ReservationId, Guid GuestId, Guid PropertyId,
    DateTimeOffset CheckIn, DateTimeOffset CheckOut,
    decimal TotalAmount, string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
```

Entities raise events via `RaiseEvent()`. The infrastructure dispatches them after `SaveChangesAsync` succeeds.

### 6.4 Typed Results (No Exceptions)

Actions return `Result<T, TError>` instead of throwing:

```csharp
public partial class CreateReservationAction : DomainAction<Guid, RoomUnavailableError>
{
    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (property is null)
            return NotFoundError.For<Guid>(nameof(Property), id);  // Not throw!
        // ...
        return reservation.Id;  // Success
    }
}
```

The SG maps `Result.IsFailure` to appropriate HTTP status codes (404 for NotFoundError, 409 for conflict, etc.).

### 6.5 Authorization

Showcase defines permission constants, roles, and policies:

```csharp
// Program.cs
app.UseAuthorization(authz =>
{
    authz.MapRole<BookingManagerRole>(r => r
        .IncludeDefinition<BookingOperator>()
        .IncludeDefinition<CatalogReader>());

    authz.MapRole<ReceptionistRole>(r => r
        .WithPermissions(
            BookingPermissions.Reservation.Read,
            BookingPermissions.Reservation.Create,
            BookingPermissions.Reservation.Update)
        .WithPermissions(BookingPermissions.Guest.All));
});
```

Actions use `[RequirePolicy<T>]` or `[RequirePermission]` for endpoint-level authorization. `[HasOwner]` on Reservation adds row-level ownership filtering.

### 6.6 Caching

Catalog entities use `[Cacheable]`:

```csharp
[Cacheable(Duration = 300)]  // 5 minutes
public partial class Property { ... }
```

Actions can invalidate cache via `[InvalidatesCache<Property>]`.

### 6.7 Jobs

Background jobs with `[RecurringJob]` and `[Job]` attributes. Jobs are SG-generated with retry, timeout, and telemetry.

### 6.8 Messaging

Message handlers with `[MessageHandler]` and `[Retry]`. ReservationConfirmedHandler demonstrates the pattern. Transport is configurable: Channels (in-process), RabbitMQ, or outbox via EF Core.

### 6.9 Sagas

The `CheckInSaga` demonstrates multi-step orchestration:

```
examples/showcase/src/Showcase.Booking/Reservations/Sagas/CheckInSaga.cs
examples/showcase/src/Showcase.Booking/Reservations/Sagas/CheckInState.cs
```

### 6.10 Internationalization

Translation keys are generated by the SG from `[assembly: TranslationKeys]`:

```
T.Property.Name, T.Property.Description
T.Reservation.Status.Pending, T.Reservation.Status.Confirmed, ...
T.Invoice.Status.Draft, T.Invoice.Status.Paid, ...
```

JSON translation files live in `src/Showcase.Host/localization/`.

---

## 7. Adding a New Feature: "Room Service"

Suppose you want to add room service ordering to the Booking boundary. Here is the step-by-step process.

### Step 1: Define the Entity

Create `examples/showcase/src/Showcase.Booking/RoomService/RoomServiceOrder.cs`:

```csharp
using Pragmatic.Persistence.Entity;

namespace Showcase.Booking.RoomService;

[Entity]
[Auditable]
[Relation.ManyToOne<Entities.Reservation>]
public partial class RoomServiceOrder : IEntity
{
    public Guid ReservationId { get; private set; }
    public Entities.Reservation Reservation { get; set; } = null!;
    public string ItemDescription { get; private set; } = "";
    public decimal Amount { get; private set; }
    public RoomServiceStatus Status { get; private set; } = RoomServiceStatus.Pending;
}
```

### Step 2: Define the Enum

Create `examples/showcase/src/Showcase.Booking/RoomService/RoomServiceStatus.cs`:

```csharp
using Pragmatic;

namespace Showcase.Booking.RoomService;

[FastEnum]
public enum RoomServiceStatus
{
    Pending,
    InProgress,
    Delivered,
    Cancelled
}
```

### Step 3: Create the Action

Create `examples/showcase/src/Showcase.Booking/RoomService/Actions/CreateRoomServiceOrderAction.cs`:

```csharp
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Showcase.Booking.Entities;

namespace Showcase.Booking.RoomService.Actions;

[DomainAction]
[Endpoint(HttpVerb.Post, "api/v1/reservations/{reservationId}/room-service")]
[Validate]
public partial class CreateRoomServiceOrderAction : DomainAction<Guid>
{
    private IRepository<RoomServiceOrder> _orders = null!;
    private IReadRepository<Reservation> _reservations = null!;

    public required Guid ReservationId { get; init; }
    public required string ItemDescription { get; init; }
    public required decimal Amount { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var reservation = await _reservations.GetByIdAsync(ReservationId, ct).ConfigureAwait(false);
        if (reservation is null)
            return NotFoundError.For<Guid>(nameof(Reservation), ReservationId);

        var order = new RoomServiceOrder
        {
            ReservationId = ReservationId,
            // ... set properties via SG-generated setters
        };

        _orders.Add(order);
        return order.Id;
    }
}
```

### Step 4: Create the DTO and Mapping

Create `examples/showcase/src/Showcase.Booking/RoomService/Dtos/RoomServiceOrderDto.cs`:

```csharp
using Pragmatic.Mapping.Attributes;

namespace Showcase.Booking.RoomService.Dtos;

[MapFrom<RoomServiceOrder>]
public partial class RoomServiceOrderDto
{
    public Guid Id { get; init; }
    public Guid ReservationId { get; init; }
    public string ItemDescription { get; init; } = "";
    public decimal Amount { get; init; }
    public RoomServiceStatus Status { get; init; }
}
```

### Step 5: Build and Verify SG Output

```bash
dotnet build examples/showcase/src/Showcase.Booking/
```

The unified SG will detect the new entity and action, generating:
- Entity setters, persistence configuration
- Action invoker, DI registration
- Endpoint handler
- Mapping methods
- Boundary interface update (adds `CreateRoomServiceOrder` to `IBookingActions`)

### Step 6: Write Unit Tests

Create `examples/showcase/tests/Showcase.Tests/Unit/RoomService/RoomServiceOrderTests.cs`:

```csharp
public class RoomServiceOrderTests
{
    [Fact]
    public void Create_WithValidData_SetsProperties()
    {
        // Test entity creation and initial state
    }
}
```

### Step 7: Write Integration Tests

Add to `examples/showcase/tests/Showcase.IntegrationTests/DomainActions/`:

```csharp
[Fact]
public async Task CreateRoomServiceOrder_ForCheckedInReservation_ReturnsCreated()
{
    // 1. Create guest, property, room type
    // 2. Create and confirm reservation
    // 3. Check in guest
    // 4. POST room service order
    // 5. Assert 201 Created
}
```

### Step 8: Rebuild and Run All Tests

```bash
dotnet build examples/showcase/
dotnet test examples/showcase/tests/Showcase.Tests/
dotnet test examples/showcase/tests/Showcase.IntegrationTests/
```

---

## 8. Testing

### 8.1 Test Projects

| Project | Type | Count | Requires |
|---------|------|-------|----------|
| `Showcase.Tests` | Unit | ~200 | Nothing |
| `Showcase.IntegrationTests` | E2E/HTTP | ~153 | Docker (Testcontainers) |

### 8.2 Integration Test Infrastructure

Three classes power integration tests:

**`PostgresFixture`** (`tests/Showcase.IntegrationTests/Infrastructure/PostgresFixture.cs`):
- Starts a PostgreSQL 17 Alpine container via Testcontainers.
- Creates both databases (`showcase_app` and `showcase_financial`).
- Applies EF Core migrations.
- Shared across all test classes via xUnit `CollectionFixture`.

**`ShowcaseWebFactory`** (`tests/Showcase.IntegrationTests/Infrastructure/ShowcaseWebFactory.cs`):
- Extends `WebApplicationFactory<Program>`.
- Overrides only connection strings to point at Testcontainers.
- Everything else (SG-generated DI, endpoints, middleware) runs exactly as in production.

**`IntegrationTestBase`** (`tests/Showcase.IntegrationTests/Infrastructure/IntegrationTestBase.cs`):
- Provides `HttpClient` with default tenant/user headers.
- Includes helper methods: `PostAsync<T>`, `GetAsync<T>`, `PutAsync`, `DeleteAsync`.
- `CreateClientAs(userId)` for testing row-level security with different users.
- `CreateClientWithPermissions(...)` for testing authorization with specific permissions.
- `CreateClientWithRoles(...)` for testing role-based access.
- `CreateAnonymousClient()` for testing unauthenticated access.
- `CreateClientWithJwt(token)` for testing JWT authentication.

### 8.3 Writing a New Integration Test

```csharp
public class MyFeatureTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MyAction_ValidInput_ReturnsExpected()
    {
        // Arrange: create prerequisites
        var guestId = await CreateGuest();
        var propertyId = await CreateProperty();

        // Act: call the endpoint
        var response = await PostAsync("/api/v1/my-endpoint?api-version=1.0", new
        {
            guestId,
            propertyId,
            // ...
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

### 8.4 Test Categories

Integration tests are organized by concern:

| Folder | What It Tests |
|--------|--------------|
| `DomainActions/` | Action execution, composite actions, versioning |
| `EntityPersistence/` | CRUD, soft delete, state machines, advanced entities |
| `Endpoints/` | HTTP routing, OpenAPI, grid search, allow anonymous |
| `Validation/` | Sync/async validation, custom validators |
| `CrossCutting/` | Concurrency, feature flags, events, boundaries, i18n, authorization, data ownership, filters, remote boundary |
| `Queries/` | Query filters, filter modes, WithoutFilter |
| `Authorization/` | JWT auth, permission enforcement |
| `Identity/` | Account endpoints (register, login) |
| `Jobs/` | Job infrastructure, store integration |

### 8.5 Common Testing Patterns

**Testing authorization denial**:
```csharp
[Fact]
public async Task RefundInvoice_WithoutPermission_Returns403()
{
    using var client = CreateClientWithPermissions("billing.invoice.read");
    var response = await PostWithClientAsync(client, $"/api/v1/invoices/{id}/refund", new { });
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

**Testing row-level security**:
```csharp
[Fact]
public async Task GetReservation_AsOtherUser_ReturnsFiltered()
{
    using var otherUser = CreateClientAs("other-user-id");
    var response = await otherUser.GetAsync($"/api/v1/reservations/{id}");
    response.StatusCode.Should().Be(HttpStatusCode.NotFound); // ownership filter
}
```

**Testing domain events**:
```csharp
[Fact]
public async Task ConfirmReservation_CreatesInvoice()
{
    // Create + confirm reservation
    await PostAsync($"/api/v1/reservations/{id}/confirm?api-version=1.0", new { });

    // Verify invoice was created in Billing (via ReservationConfirmedHandler)
    var invoices = await GetAsync<List<InvoiceDto>>("/api/v1/invoices?api-version=1.0");
    invoices.Should().ContainSingle(i => i.ReservationId == id);
}
```

### 8.6 Running Specific Test Categories

```bash
# Only unit tests
dotnet test examples/showcase/tests/Showcase.Tests/ --filter "Category!=Integration"

# Only integration tests
dotnet test examples/showcase/tests/Showcase.IntegrationTests/

# Specific test class
dotnet test examples/showcase/tests/Showcase.IntegrationTests/ --filter "FullyQualifiedName~DomainActionTests"

# Specific test method
dotnet test examples/showcase/tests/Showcase.IntegrationTests/ --filter "CreateReservation_ValidInput_ReturnsCreated"
```

---

## 9. Key File Reference

### Boundaries

| File | Purpose |
|------|---------|
| `src/Showcase.Catalog/CatalogBoundary.cs` | Catalog boundary declaration |
| `src/Showcase.Booking/BookingBoundary.cs` | Booking boundary (+ ReadAccess to Catalog) |
| `src/Showcase.Billing/BillingBoundary.cs` | Billing boundary |

### Host Configuration

| File | Purpose |
|------|---------|
| `src/Showcase.Host/Program.cs` | `PragmaticApp.RunAsync` with builder config |
| `src/Showcase.Host/ShowcaseStartupStep.cs` | Business services and HTTP pipeline |

### Core Domain Files

| File | Purpose |
|------|---------|
| `src/Showcase.Booking/Reservations/Reservation.cs` | Main entity with full trait composition |
| `src/Showcase.Booking/Reservations/Enums/ReservationStatus.cs` | State machine enum |
| `src/Showcase.Booking/Reservations/Actions/CreateReservationAction.cs` | Domain action with validation, versioning, feature flags |
| `src/Showcase.Billing/EventHandlers/ReservationConfirmedHandler.cs` | Cross-boundary event handler |
| `src/Showcase.Booking/Events/ReservationConfirmed.cs` | Event record carrying all needed data |

### Test Infrastructure

| File | Purpose |
|------|---------|
| `tests/Showcase.IntegrationTests/Infrastructure/PostgresFixture.cs` | Testcontainers PostgreSQL |
| `tests/Showcase.IntegrationTests/Infrastructure/ShowcaseWebFactory.cs` | WebApplicationFactory override |
| `tests/Showcase.IntegrationTests/Infrastructure/IntegrationTestBase.cs` | Base class with HTTP helpers |

### Distributed Deployment

| File | Purpose |
|------|---------|
| `src/Showcase.Host.Distributed/ShowcaseDistributedModule.cs` | Module with `[RemoteBoundary<BillingModule>]` |
| `src/Showcase.Billing.Host/Program.cs` | Standalone Billing host |
