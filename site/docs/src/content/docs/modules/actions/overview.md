---
title: "Pragmatic.Actions"
description: "Source-generated CQRS-style domain actions for .NET 10. Declare the operation; the generator writes"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Actions/README.md
sidebar:
  order: 0
  label: Overview
---
Source-generated CQRS-style domain actions for .NET 10. Declare the operation; the generator writes
the pipeline (DI, validation, authorization, telemetry, persistence) at compile time, with no
reflection on the executed path.

## The Problem

Every business operation repeats the same ceremony: resolve dependencies, validate, check
authorization, execute, handle errors, persist, log, record telemetry. Across a service with 10+
methods, the plumbing dwarfs the logic.

```csharp
// Without Pragmatic: 60+ lines per operation, most of it plumbing
public async Task<Result<Guid>> CreateReservationAsync(
    CreateReservationRequest request, ClaimsPrincipal user, CancellationToken ct)
{
    if (!(await auth.AuthorizeAsync(user, "booking.reservation.create")).Succeeded)
        return Result<Guid>.Failure(ForbiddenError.MissingPermission("booking.reservation.create"));
    var validation = await validator.ValidateAsync(request, ct);
    if (!validation.IsValid) return Result<Guid>.Failure(BadRequestError.Create(validation.ToString()));
    logger.LogInformation("Creating reservation for {GuestId}", request.GuestId);
    // ... business logic ...
    reservations.Add(reservation);
    await unitOfWork.SaveChangesAsync(ct);
    return reservation.Id;
}
// ...repeated for every method.
```

## The Solution

Declare **what** the operation does; the generator handles **how**.

```csharp
[DomainAction]
[RequirePolicy<ReservationManagementPolicy>]
[Endpoint(HttpVerb.Post, "api/reservations")]
public partial class CreateReservationAction : DomainAction<Guid, NotFoundError, RoomUnavailableError>
{
    private IRepository<Reservation> _reservations = null!;   // injected by the generator
    private IReadRepository<Property> _properties = null!;

    public required CreateReservationRequest Request { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var property = await _properties.GetByIdAsync(Request.PropertyId, ct);
        if (property is null) return NotFoundError.For(nameof(Property), Request.PropertyId);

        var reservation = Reservation.Create(/* ... */);
        _reservations.Add(reservation);
        return reservation.Id;
    }
}
```

The generator produces the invoker pipeline, DI/field injection, authorization enforcement, validation,
telemetry, and persistence. For entity CRUD it collapses even further: a `Mutation<T>` is the whole
class (property mapping, lifecycle, validation, persistence, DI all generated):

```csharp
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(CatalogPermissions.Amenity.Create)]
[Endpoint(HttpVerb.Post, "api/amenities")]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
    public AmenityCategory Category { get; init; }
}
```

## Three base classes

| Base class | For | Returns |
|------------|-----|---------|
| `DomainAction<TReturn>` (`<TReturn, TError>`) | Custom logic: orchestrate repos, compute, call services | `Result<TReturn, …>` |
| `VoidDomainAction` | Commands with no return value | `VoidResult` |
| `Mutation<TEntity>` | Entity CRUD (create/update/delete), minimal boilerplate | the entity |

Operation taxonomy (Mutation / SideEffect / Query) and the pipeline (filters → execute → save →
after-hooks) are covered in [Concepts](/modules/actions/concepts/).

## Installation

```bash
dotnet add package Pragmatic.Actions
dotnet add package Pragmatic.SourceGenerator   # the unified analyzer
```

(Building inside this monorepo? See [Monorepo Structure](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/monorepo-structure.md).)

## What the generator gives you

- **Invoker pipeline**: before/after filters, validation, authorization, save ordering, telemetry.
- **DI & field injection**: private fields resolved automatically; no constructor boilerplate.
- **Authorization**: `[RequirePermission]` / `[RequirePolicy]` enforced in the pipeline.
- **Validation**: runs before `Execute()` by default, the synchronous rules first and the async
  validators declared for it after; `[Validate]` only changes that default and `[NoValidation]`
  switches it off.
- **Boundaries**: the namespace decides the boundary (and the sub-boundary group); `[BelongsTo<T>]`
  names it explicitly, and it keys the unit of work / DbContext.
- **Action versioning**, **entity pre-loading**, and **composite (single-transaction) actions**.

## Status

**Functional** within 1.0.0-alpha: the DomainAction/Mutation pipeline, filters, authorization,
validation, boundaries, and versioning. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/actions/concepts/) | Pipeline lifecycle, operation taxonomy, base-class decision guide, composite actions |
| [Getting Started](/modules/actions/getting-started/) | Your first DomainAction and Mutation |
| [Mutations](/modules/actions/mutations/) | `Mutation<T>`, modes, property mapping, entity lifecycle |
| [Pipeline](/modules/actions/pipeline/) | Filters, ordering, authorization, validation, save/event ordering |
| [Boundaries](/modules/actions/boundaries/) | `[BelongsTo<T>]`, sub-boundaries, keyed unit of work |
| [Common Mistakes](/modules/actions/common-mistakes/) | The most frequent action pitfalls |
| [Troubleshooting](/modules/actions/troubleshooting/) | Problem/solution guide with diagnostics |

## Cross-module integration

Actions are invoked by [Endpoints](/modules/endpoints/overview/), persist via
[Persistence](/modules/persistence/overview/), validate via
[Validation](/modules/validation/overview/), authorize via
[Authorization](/modules/authorization/overview/), and are wired by
[Composition](/modules/composition/overview/).

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/actions/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Actions is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
