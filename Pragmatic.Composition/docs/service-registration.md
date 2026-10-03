# Service Registration

This guide covers all the ways to register services in Pragmatic.Composition: the `[Service]` attribute family, decorators, factories, property injection, and assembly scanning.

## [Service] -- Attribute-Based Registration

The simplest way to register a service. The source generator emits compile-time registration code -- no reflection at runtime.

### Basic Usage

```csharp
[Service]
public class OrderService : IOrderService { /* ... */ }
```

Default behavior:
- **Lifetime**: `Scoped`
- **Service type**: First implemented interface
- If the class implements no interfaces, the SG reports a diagnostic. Use `AsSelf = true` for concrete registration.

### Generic Attribute (Explicit Interface)

When a class implements multiple interfaces, use `[Service<TInterface>]` to pick one:

```csharp
[Service<IPaymentProvider>(Key = "stripe")]
public class StripePaymentProvider : IPaymentProvider, IDisposable { /* ... */ }
```

This registers as `IPaymentProvider` only, ignoring `IDisposable`.

### ServiceAttribute Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Lifetime` | `ServiceLifetime` | `Scoped` | `Singleton`, `Scoped`, or `Transient` |
| `As` | `Type?` | `null` | Register as this type (prefer `ServiceAttribute<T>`) |
| `AsSelf` | `bool` | `false` | Register as concrete type instead of interface |
| `Key` | `string?` | `null` | Keyed service registration (.NET 8+) |

### ServiceLifetime Enum

Defined in `Pragmatic.Composition.Attributes` (mirrors `Microsoft.Extensions.DependencyInjection.ServiceLifetime`):

| Value | Behavior |
|-------|----------|
| `Singleton` | One instance shared across all requests |
| `Scoped` | One instance per scope (HTTP request) -- **default** |
| `Transient` | New instance every time |

### Examples

```csharp
// Scoped (default), registered as IOrderService
[Service]
public class OrderService : IOrderService { }

// Singleton, registered as concrete type
[Service(AsSelf = true, Lifetime = Lifetime.Singleton)]
public class CacheWarmupService { }

// Keyed service (explicit interface via generic)
[Service<IPaymentProvider>(Key = "stripe")]
public class StripePaymentProvider : IPaymentProvider { }

// Transient
[Service(Lifetime = Lifetime.Transient)]
public class NotificationFormatter : INotificationFormatter { }
```

## [Decorator] -- Service Decoration

Decorators wrap an existing service, adding cross-cutting behavior. They must implement the same interface as the service they decorate.

### Usage

```csharp
[Decorator(Order = 1)]
public class LoggingReservationPricingService(
    IReservationPricingService inner,
    ILogger<LoggingReservationPricingService> logger) : IReservationPricingService
{
    public decimal CalculatePrice(Reservation reservation)
    {
        logger.LogInformation("Calculating price for reservation {Id}", reservation.Id);
        var price = inner.CalculatePrice(reservation);
        logger.LogInformation("Price calculated: {Price}", price);
        return price;
    }
}
```

### Order Semantics

The `Order` property determines decoration order:
- **Lower order** = closer to the real service (applied first, called last on the way in)
- **Higher order** = outermost (first to receive calls)

```
Call flow:  Outer (Order=2) --> Inner (Order=1) --> RealService
Return:     Outer (Order=2) <-- Inner (Order=1) <-- RealService
```

### Requirements

- The decorator must implement the same interface as the decorated service.
- The constructor must accept the inner service as a parameter (by its interface type).
- Additional constructor parameters are resolved from DI.

### Manual Decoration

For cases where attribute-based decoration is not suitable, use the `Decorate<TService, TDecorator>()` extension method:

```csharp
// In IStartupStep.ConfigureServices
services.Decorate<IOrderService, CachingOrderService>();
```

Or with a factory:

```csharp
services.Decorate<IOrderService>((inner, sp) =>
{
    var logger = sp.GetRequiredService<ILogger<LoggingOrderService>>();
    return new LoggingOrderService(inner, logger);
});
```

The `Decorate` methods live in `ServiceCollectionDecorateExtensions` in `Pragmatic.Abstractions`, so domain modules can use them without referencing ASP.NET Core.

## [ServiceFactory] / [Factory] -- Factory Methods

For services that need custom construction logic, use factory classes:

```csharp
[ServiceFactory]
public class InfrastructureFactories
{
    [Factory(Lifetime = Lifetime.Singleton)]
    public IDbConnection CreateConnection(IConfiguration config)
    {
        var connStr = config.GetConnectionString("Default");
        return new SqlConnection(connStr);
    }

    [Factory(Lifetime = Lifetime.Scoped)]
    public IFileStorage CreateStorage(IHostEnvironment env, ILogger<LocalDiskFileStorage> logger)
    {
        var basePath = Path.Combine(env.ContentRootPath, "uploads");
        return new LocalDiskFileStorage(basePath, logger);
    }
}
```

Rules:
- `[ServiceFactory]` marks the container class. The class itself is registered as a singleton.
- `[Factory]` marks individual factory methods.
- The return type of each method becomes the registered service type.
- Method parameters are resolved from DI.
- `FactoryAttribute.Lifetime` controls the lifetime of the created service (default: `Scoped`).
- The host wires them through the generated `AddPragmaticServiceFactories()`: its own factories, and those a referenced boundary library declares (the library publishes them as metadata and the host aggregates them).

## [Inject] -- Property and Method Injection

For optional dependencies or post-construction initialization. The class must also be marked with `[Service]`.

### Property Injection

```csharp
[Service]
public class NotificationService : INotificationService
{
    // Optional -- null if IEmailSender is not registered
    [Inject]
    public IEmailSender? EmailSender { get; set; }

    // Required -- throws if not available
    [Inject(Required = true)]
    public IMessageQueue Queue { get; set; } = null!;

    // Keyed service
    [Inject(Key = "priority")]
    public IMessageQueue? PriorityQueue { get; set; }
}
```

### InjectAttribute Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Required` | `bool` | `false` | Throw if service not resolvable |
| `Key` | `string?` | `null` | Keyed service key |

### When to Use

- **Prefer constructor injection** for required dependencies (the normal pattern).
- Use `[Inject]` for **optional** dependencies that may or may not be registered.
- Use `[Inject]` to break **circular dependency** chains.
- Use `[Inject(Key = "...")]` for **keyed services** on properties.

## [ProvidedByHost] -- A Contract the Host Fills

`PRAG1641` reports a dependency that nothing the generator can see registers. Some contracts are filled
only by the host: a package declares what it needs, and the implementation comes from another package the
host chooses, or from the host's own code. The generator running on the module cannot see that
registration, so the contract says so itself:

```csharp
[ProvidedByHost(Lifetime.Singleton)]        // UseStorage() registers it
public interface IFileStorage { /* ... */ }

[ProvidedByHost(Lifetime.Scoped)]           // one per request
public interface ITenantContext { /* ... */ }

[ProvidedByHost]                            // the host decides: Validation takes the lifetime as an argument
public interface IValidator<in T> { /* ... */ }
```

A dependency on a type marked `[ProvidedByHost]` (by constructor, method or property) is not reported.
The attribute is on the contract, not on the consumer: every module that depends on it is covered, and a
dependency on anything else is still checked. If the host forgets the registration, resolving the
consumer fails, as for any missing service.

**Name the lifetime.** `PRAG1642` refuses a singleton that captures a shorter-lived dependency, and it
can only do that for a host-filled contract if the contract says which lifetime the host registers it
with. Leave the argument out only when the answer genuinely depends on the host.

**The gate counts the ones that do not.** `a contract a package registers says who registers it` runs
in `--tier full` over every Pragmatic package's `src`: an interface this repository declares, that a
package registers and whose file carries no `[ProvidedByHost]`, is in that count, and the count may
fall and never rise. It is a population to triage rather than a bug list (a contract only the
framework resolves is registered the same way), but a **new** one cannot arrive unnoticed.

**Where the framework's own contracts say it.** Every Pragmatic contract a `Use*` call registers carries
this attribute, in the package that registers it: `IFileStorage`, `IEmailSender`, `IStringLocalizer`,
`IClock`, `ICacheStack`, `ITenantContext`, `ITenantStore`, `ICurrentUser`, `IUnitOfWork`,
`IRepository<T>`, `IQueryFilter<T>`, the action invokers, and the rest. The attribute lives on the
contract rather than in a list of names inside the generator, because such a list is one that whoever
adds a `Use*` extension in another package has to remember: nothing fails when they forget, and the
failure lands on the application that uses the package.

## A Contract Another Module Registers

The same question, one floor over: a host composes several modules, and a `[Service]` of one may depend
on a contract another one registers. Nothing is needed for this: each module's generated DI metadata
already says what it registers, and the validator reads it from the reference. That covers a sibling's
`[Service]` classes and the `I{Module}Reads` interface a `[Published]` query generates, which is
registered by the generated `Add{Module}Reads()` the host calls.

The lifetime travels with the name, so a singleton capturing another module's scoped service is still
reported.

## Assembly Scanning

For convention-based registration without attributes. The fluent API is accessible via `services.Scan()`:

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<OrderService>()
    .AddClasses(filter => filter.AssignableTo<ICommandHandler>())
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

### Assembly Sources

| Method | Description |
|--------|-------------|
| `FromAssemblyOf<T>()` | Assembly containing type `T` |
| `FromAssemblies(params Assembly[])` | Explicit assembly list |
| `FromAssembliesMatching(string pattern)` | Glob pattern on DLL filenames (e.g., `"MyApp.*"`) |
| `FromCallingAssembly()` | Calling assembly |
| `FromEntryAssembly()` | Entry assembly |
| `FromDependencyContext(predicate?)` | `DependencyContext.Default` runtime libraries |
| `FromDependencyContext(string prefix)` | Libraries whose name starts with prefix |

### Type Filters

Applied within `AddClasses(filter => ...)`:

| Method | Description |
|--------|-------------|
| `AssignableTo<T>()` | Implements `T` (supports open generics like `IRepository<>`) |
| `AssignableTo(Type type)` | Same, non-generic |
| `WithAttribute<T>()` | Has specific attribute |
| `InNamespace(string ns)` | Exact namespace match |
| `InNamespaceOf<T>()` | Namespace starts with `T`'s namespace |
| `Where(Func<Type, bool>)` | Custom predicate |
| `NotWhere(Func<Type, bool>)` | Exclude by predicate |

Filters are composable -- they chain as AND conditions:

```csharp
scan.FromAssemblyOf<OrderService>()
    .AddClasses(f => f
        .AssignableTo<IEventHandler>()
        .InNamespace("MyApp.Booking.Handlers")
        .NotWhere(t => t.Name.Contains("Test")))
    .AsImplementedInterfaces()
    .WithScopedLifetime();
```

### Registration Modes

| Method | Description |
|--------|-------------|
| `AsImplementedInterfaces()` | Register for all implemented interfaces |
| `AsSelf()` | Register as concrete type |
| `AsSelfWithInterfaces()` | Register as concrete type AND all interfaces |
| `As<T>()` | Register as specific type |
| `As(Type type)` | Register as specific type (non-generic) |
| `As(Func<Type, Type> selector)` | Custom type selector |
| `AsMatchingInterface()` | Register as `I{ClassName}` (convention-based) |

### Registration Strategies

Control what happens when a service type is already registered:

```csharp
scan.FromAssemblyOf<OrderService>()
    .AddClasses(f => f.AssignableTo<IRepository>())
    .AsImplementedInterfaces()
    .UsingRegistrationStrategy(RegistrationStrategy.Skip)
    .WithScopedLifetime();
```

| Strategy | Behavior |
|----------|----------|
| `Append` | Always add (default). Multiple implementations for same interface. |
| `Skip` | Skip if service type already registered |
| `Replace` | Remove existing registrations, add new one |
| `Throw` | Throw `InvalidOperationException` on duplicate |

### Direct Type Registration

For registering specific types without filtering:

```csharp
scan.FromAssemblyOf<OrderService>()
    .AddType<SpecialService>()
    .AsSelf()
    .WithSingletonLifetime();

scan.FromAssemblyOf<OrderService>()
    .AddTypes<ServiceA, ServiceB, ServiceC>()
    .AsImplementedInterfaces()
    .WithScopedLifetime();
```

### Error Handling

The `Scan` method accepts an optional error callback for logging assembly load failures:

```csharp
services.Scan(
    scan => scan.FromAssembliesMatching("MyApp.*").AddClasses().AsImplementedInterfaces().WithScopedLifetime(),
    onScanError: (message, ex) => logger.LogWarning(ex, message));
```

## TryAddService

For conditional registration (add only if not already registered):

```csharp
services.TryAddService<IOrderService, OrderService>(ServiceLifetime.Scoped);
```

This is useful for default implementations that should be overridable.

## Source-Generated vs Runtime Registration

| Feature | Attribute-Based (SG) | Scanning (Runtime) |
|---------|----------------------|-------------------|
| When | Compile-time | Runtime (startup) |
| Reflection | None | Assembly scanning uses reflection |
| Cross-assembly | Via `[PragmaticMetadata]` | Via assembly loading |
| Best for | Known services | Convention-based bulk registration |
| Performance | Zero overhead | Small startup cost |

**Recommendation**: Use `[Service]` and `[Decorator]` for all known services. Use scanning only for convention-based scenarios where attribute decoration is impractical.
