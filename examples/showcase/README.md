# Pragmatic.Showcase

> International Hotel Booking Platform — Full demo of the Pragmatic.Design ecosystem.

## Quick Start

```bash
dotnet run --project src/Showcase.Host
# Open http://localhost:5000/scalar  (Scalar API UI)
```

> No database setup needed — uses InMemory EF Core.
> Demo data (3 hotels + 2 guests) is seeded automatically at startup.

## Architecture

```
Showcase.Catalog   →  Hotels, RoomTypes, Amenities, CancellationPolicies
Showcase.Booking   →  Reservations, Guests, RoomAssignments
Showcase.Billing   →  Invoices, Payments, LineItems  [isolated financial database]
Showcase.Host      →  ASP.NET Core host, Pipeline, DI wiring, Demo seeding
```

### Database Topology

Two InMemory databases demonstrating compliance separation:

| Database | Modules | Purpose |
|----------|---------|---------|
| `ShowcaseAppDatabase` | Catalog + Booking | Operational — shared for SQL joins |
| `ShowcaseFinancialDatabase` | Billing | Financial — isolated for audit/compliance |

Billing receives data via **domain events** (`ReservationConfirmed → CreateInvoice`), never via SQL join.

### Demo Flow

```
POST /api/guests                         → Register a guest (or use seeded ones)
POST /api/reservations                   → CreateReservationAction
POST /api/reservations/{id}/confirm      → Confirmed → auto-creates Invoice
POST /api/invoices/{id}/pay              → MarkInvoicePaid → InvoicePaid event
POST /api/reservations/{id}/checkin      → CheckedIn (ICurrentUser audit)
POST /api/properties/{id}/photos            → Upload photo (IFileStorage demo)
```

## Module Coverage Matrix

| Pragmatic Module | Catalog | Booking | Billing | Host |
|-----------------|---------|---------|---------|------|
| **Result** | - | Multi-error, Match | VoidResult | - |
| **Ensure** | Entity guards | Action guards | Entity guards | - |
| **Validation** | Range, NotWhiteSpace | FutureDate, GreaterThan, AsyncValidator | - | - |
| **Mapping** | MapFrom, Projection | MapFrom, MapTo, MapProperty | MapFrom, MapConverter | - |
| **Actions** | UpdateProperty, Boundary | Create/Confirm/Cancel/CheckIn, Boundary, BelongsTo | CreateInvoice(Internal), MarkPaid, Boundary, BelongsTo | - |
| **Endpoints** | AutoCrud, Autocomplete, RequirePermission | Manual, AllowAnonymous, RateLimit, RequireAnyPermission | Manual | Groups, Pre/PostProcessor |
| **Persistence** | Entity, Auditable, SoftDelete, LogicKey | ConcurrencyAware, GeneratedValue | GeneratedValue | DbContext |
| **Caching** | Cacheable, InvalidatesCache | - | - | - |
| **Specification** | ActiveProperty, ByCity, ByRating | Overlapping, Search, ForGuest | - | - |
| **Events** | - | ReservationCreated/Confirmed/Cancelled | InvoicePaid, Handler | Event DI |
| **Temporal** | - | IClock | - | - |
| **I18n** | LocalizedString, T.Property.xxx (SG) | T.Reservation.Status.xxx (SG) | T.Invoice.Status.xxx (SG) | - |
| **Storage** | IFileStorage (UploadPhoto) | - | - | LocalDiskFileStorage |
| **Identity** | - | RequireAnyPermission | - | ICurrentUser |
| **MultiTenancy** | - | - | - | ITenantContext |
| **Composition** | Module, BelongsTo | Module, DependsOn, Service, Decorator | Module, DependsOn, Service(Key) | StartupStep, IPragmaticBuilder, Service(Singleton), Inject |

## Domain Modules

### Catalog
- **Entities**: Property, RoomType, Amenity, CancellationPolicy, PropertyAmenity
- **Endpoints**: AutoCrud (Property, Amenity), Autocomplete, ImportSeasonalRates
- **Features**: Caching, Specification, Soft Delete

### Booking
- **Entities**: Reservation, Guest, RoomAssignment
- **Endpoints**: CreateReservation, ConfirmReservation, CancelReservation, CheckInGuest, SearchAvailableRooms
- **Features**: Domain Events, Concurrency Control, Async Validation, Boundary

### Billing
- **Entities**: Invoice, Payment, LineItem
- **Endpoints**: CreateInvoice (internal), MarkInvoicePaid
- **Features**: Cross-module events, Keyed Services (payment providers)

## Key Patterns Demonstrated

### Source Generator Pipeline
```
[Entity] → [MapFrom] → [Mutation] → [DomainAction] → [Endpoint]
   ↓           ↓            ↓             ↓               ↓
 Setters   FromEntity   ApplyToAsync   Execute()      HandleAsync()
```

### Full-AOT JSON (SG-emitted context)

The domain modules (`Catalog`, `Booking`, `Billing`, `Accounts`) opt into the generated
`JsonSerializerContext` via `<PragmaticGenerateJsonContext>true</>` in their `.csproj`. Each module's SG
emits a `PragmaticJsonContext` covering its boundary types — including **positional `record` events** (e.g.
`InvoicePaid`, whose inherited init-only properties are assigned through `[UnsafeAccessor]`). The host
aggregates every module's context (see `Host.Services.g.cs` → `AddGeneratedJsonContext`) into the shared
`PragmaticJsonOptions` seam, so outbox/transport/job payloads and HTTP responses serialize without
reflection. `Showcase.Tests/Serialization/GeneratedJsonContextTests` round-trips a real record event with
the reflection fallback **disabled**. Details: [`docs/howto/aot-and-trimming.md`](../../docs/howto/aot-and-trimming.md).

### Boundary Pattern
```csharp
// Define a boundary — captures all actions in its namespace
[Boundary]
public partial class BookingBoundary;

// Actions belong to the boundary via [BelongsTo] or namespace matching
[DomainAction]
[BelongsTo<BookingBoundary>]
public partial class CreateReservationAction : DomainAction<Guid> { ... }

// Generated: IBookingActions — typed facade for cross-module invocation
public interface IBookingActions
{
    IDomainActionInvoker<CreateReservationAction, Guid> CreateReservation { get; }
    IVoidDomainActionInvoker<ConfirmReservationAction> ConfirmReservation { get; }
}

// DI: services.AddBookingBoundary();
```

### Result Pattern
```csharp
DomainAction<Guid, RoomUnavailableError>  // Typed errors
VoidDomainAction<NotFoundError>           // Void with error
result.Match(onSuccess: ..., onFailure: ...)  // Railway
```

### Pre/Post Processors
```
Request → TenantValidation(Pre) → HandleAsync → AuditLog(Post) → Response
```

### Decorator Chain
```
Caller → LoggingPricingService(2) → PricingService(1)
```

## Known Limitations & Trade-offs

### Cross-Boundary Entity References
The Showcase uses direct entity references across module boundaries (e.g., Booking references
Catalog's `Property` and `RoomType`). In a production modular monolith, modules should
communicate via events and maintain local read models (Anti-Corruption Layer pattern).

This is an intentional simplification for the demo. A real system would:
- Define boundary-local projections (e.g., `BookableRoom` in Booking)
- Populate them via domain events from Catalog (`RoomTypeUpdated`)
- Reference only IDs across boundaries

### Shared Database
All modules share a single InMemory database for demo simplicity. Production deployments
should use separate schemas or databases per boundary to enforce data isolation.

## Dev Environment

See [ENV.md](ENV.md) for ports, database setup, and curl examples.
