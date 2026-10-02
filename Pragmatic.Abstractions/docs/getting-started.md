# Getting Started with Pragmatic.Abstractions

This guide walks you through referencing Pragmatic.Abstractions and using its interfaces in your code. By the end, you will understand the layer model, know which interfaces to use for common scenarios, and be able to wire up implementations via DI.

## Prerequisites

- .NET 10 SDK installed
- A Pragmatic.Design project (or a new .NET project)

---

## Step 1: Add the Package Reference

```bash
dotnet add package Pragmatic.Abstractions
```

Or in your `.csproj`:

```xml
<PackageReference Include="Pragmatic.Abstractions" />
```

This gives you access to all shared interfaces, attributes, and contracts. No ASP.NET Core or EF Core dependencies are pulled in.

---

## Step 2: Understand the Layer Model

Pragmatic.Design uses a strict layering model. Abstractions sits at Layer 0 -- the foundation that every other module depends on.

```
Layer 0 (Foundation)           You are here: Pragmatic.Abstractions
    |
Layer 1 (Capabilities)        Validation, Mapping, Specification, Caching, FeatureFlags, ...
    |
Layer 2 (Integration)         Actions, Endpoints, Persistence.EFCore, Composition, ...
```

**Rule:** Lower layers never reference higher layers. Abstractions never references Actions, Persistence, or any Layer 1/2 module. This prevents circular dependencies.

**Consequence:** When you write domain code that depends on `IRepository<T>`, you reference Abstractions for the interface. The actual EF Core implementation is wired up at composition time in the host project. Your domain code never knows about EF Core.

---

## Step 3: Use Interfaces in Domain Code

### Repository Access

```csharp
using Pragmatic.Persistence.Repository;

public class InvoiceService(
    IReadRepository<Invoice> invoices,
    IRepository<Invoice> repository)
{
    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct)
        => await invoices.GetByIdAsync(id, ct);

    public void Add(Invoice invoice)
        => repository.Add(invoice);
}
```

The `IReadRepository` and `IRepository` interfaces are declared in `Pragmatic.Persistence`, in the namespace `Pragmatic.Persistence.Repository`. The EF Core implementation is registered by the source generator when `Pragmatic.Persistence.EFCore` is referenced in the host project.

### Current User

```csharp
using Pragmatic.Identity;

public class OrderService(ICurrentUser currentUser)
{
    public async Task<Result<Order, ForbiddenError>> CreateOrder(CreateOrderRequest request)
    {
        if (!currentUser.Authorization.HasPermission("orders.create"))
            return new ForbiddenError();

        // currentUser.Id, currentUser.TenantId, etc.
    }
}
```

`ICurrentUser` is defined in Abstractions. In HTTP contexts, `Pragmatic.Identity.AspNetCore` provides `ClaimsPrincipalUserAccessor`. In background jobs, use `SystemUser.Instance`.

### Clock

```csharp
using Pragmatic.Temporal.Clock;

public class ReservationService(IClock clock)
{
    public bool IsExpired(Reservation reservation)
        => reservation.ExpiresAt < clock.UtcNow;
}
```

`IClock` is defined in Abstractions. `Pragmatic.Temporal` provides `SystemClock` for production; `TestClock` is in `Pragmatic.Temporal.Testing`, a separate package, so a test project references that one and production never carries it.

### Domain Events

```csharp
using Pragmatic.Events;

public class PaymentService(IDomainEventDispatcher events)
{
    public async Task ProcessPaymentAsync(Payment payment, CancellationToken ct)
    {
        // ... payment logic ...
        await events.DispatchAsync(new PaymentCompleted(payment.Id, payment.Amount));
    }
}
```

### Tenant Context

```csharp
using Pragmatic.MultiTenancy;

public class TenantAwareService(ITenantContext tenant)
{
    public string GetCacheKey(string key)
        => tenant.IsResolved ? $"{tenant.TenantId}:{key}" : key;
}
```

### Caching

```csharp
using Pragmatic.Caching;

public class ProductService(ICacheStack cache)
{
    public async Task<Product?> GetProductAsync(Guid id, CancellationToken ct)
    {
        return await cache.GetOrSetAsync(
            $"product:{id}",
            async _ => await LoadFromDatabase(id, ct),
            CacheEntryOptions.WithDuration(TimeSpan.FromMinutes(10)));
    }
}
```

---

## Step 4: Use Attributes for Source Generation

Abstractions defines the attributes that the source generator reads at compile time.

### Module Declaration

```csharp
using Pragmatic.Composition.Attributes;

[Module(Name = "Booking", Version = "1.0")]
public class BookingModule;
```

### Service Registration

```csharp
using Pragmatic.Composition.Attributes;

[Service<IPaymentGateway>(Lifetime = Lifetime.Scoped)]
public class StripePaymentGateway : IPaymentGateway
{
    // The SG registers this in DI automatically
}
```

### Permission Declaration

```csharp
using Pragmatic.Authorization;

[RequirePermission("booking.reservation.create")]
public partial class CreateReservationAction : DomainAction<Guid>
{
    // The SG generates permission enforcement in the pipeline
}
```

---

## Step 5: Use Null-Object Singletons

Abstractions provides safe defaults for optional dependencies:

```csharp
// Anonymous user -- all permission checks return false
ICurrentUser anonymous = AnonymousUser.Instance;
anonymous.IsAuthenticated;  // false
anonymous.Authorization.HasPermission("anything");  // false

// Unresolved tenant -- no tenant context
ITenantContext unresolved = UnresolvedTenantContext.Instance;
unresolved.IsResolved;  // false
unresolved.TenantId;    // null

// Full access -- all permission checks return true (for system/background contexts)
IUserAuthorization fullAccess = FullAccessUserAuthorization.Instance;
fullAccess.HasPermission("anything");  // true

// Null authentication -- no auth metadata
IAuthenticationContext nullAuth = NullAuthenticationContext.Instance;
nullAuth.Scheme;   // null
nullAuth.Issuer;   // null
```

---

## Step 6: Wire Up Implementations

In the host project (the one that actually runs), add the runtime packages and let the source generator connect everything:

```csharp
// Program.cs
await PragmaticApp.RunAsync(args, app =>
{
    // Identity (provides ICurrentUser)
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");

    // Multi-tenancy (provides ITenantContext)
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Storage (provides IFileStorage). Environment is IHostEnvironment, so ContentRootPath —
    // WebRootPath lives on IWebHostEnvironment, which the builder deliberately does not expose.
    app.UseStorage(sp => new LocalDiskFileStorage(
        Path.Combine(app.Environment.ContentRootPath, "uploads"),
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

The source generator reads `[Module]`, `[Include<T>]`, `[PragmaticDatabase]` attributes and generates the DI wiring automatically. Most interfaces are registered without manual `services.Add*()` calls.

---

## Common Patterns

### Depend on Interfaces, Not Implementations

```csharp
// Domain module -- references only Pragmatic.Abstractions
public class BookingService(
    IRepository<Reservation> reservations,  // Interface
    ICurrentUser currentUser,                       // Interface
    IClock clock)                                    // Interface
{
    // No reference to EF Core, ASP.NET Core, or any runtime package
}
```

### Test with Mocks

```csharp
[Fact]
public async Task CreateReservation_SetsCreatedBy()
{
    var user = Substitute.For<ICurrentUser>();
    user.Id.Returns("user-42");

    var clock = Substitute.For<IClock>();
    clock.UtcNow.Returns(new DateTimeOffset(2026, 3, 27, 12, 0, 0, TimeSpan.Zero));

    var repo = Substitute.For<IRepository<Reservation>>();
    var service = new BookingService(repo, user, clock);

    // Act and assert...
}
```

### Switch Implementations per Environment

```csharp
// Development: in-memory everything
services.AddSingleton<IFeatureFlagStore, InMemoryFeatureFlagStore>();
services.AddSingleton<IConfigurationStore, InMemoryConfigurationStore>();

// Production: real backends. Registered through their own extension, not by naming the type —
// the implementations are internal, which is what lets them change without a breaking release.
services.AddSingleton<IFeatureFlagStore, ConfigurationFeatureFlagStore>();
services.AddAzureAppConfigurationStore(o => o.Endpoint = "https://…");
```

The domain code never changes. Only the composition root (host project) differs.

---

## What Belongs in Abstractions vs. Modules

| Belongs in Abstractions | Belongs in a Module |
|---|---|
| Interface definitions (`IRepository`, `IClock`) | Concrete implementations (the generated `Invoice.Repository`, `SystemClock`) |
| Attribute definitions (`[Service]`, `[Module]`) | Source generator that reads the attributes |
| Enums (`PrincipalKind`, `DatabaseProvider`) | Runtime logic (middleware, interceptors) |
| Null-object singletons (`AnonymousUser`) | Full implementations (`ClaimsPrincipalUserAccessor`) |
| Record types for cross-module data (`PermissionInfo`) | Business logic classes |

---

## Next Steps

- Read [Concepts](concepts.md) for the full architecture and interface catalog
- Read [Interfaces](interfaces.md) for member-level documentation of every interface
- Read [Design Principles](design-principles.md) for the rules governing what goes into Abstractions
- Browse the [Pragmatic.Design README](../../README.md) for the full module catalog
