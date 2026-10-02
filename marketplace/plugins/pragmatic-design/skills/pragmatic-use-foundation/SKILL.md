---
name: pragmatic-use-foundation
description: Use when working with Result and error handling, Ensure guards, validation attributes, object mapping, dates and the clock, specifications or partial-update Patch — the Pragmatic.Design foundation libraries.
---

# Pragmatic Use Foundation

**Covers:** Use the Pragmatic.Design foundation libraries from NuGet in consumer apps — Result, Ensure, Validation, Mapping, Temporal, Specification, Patch — with the patterns driven by the source generator.

Cross-cutting libraries that support every feature of a Pragmatic app. Ensure and Specification are pure runtime. Validation, Mapping and Patch are **attribute-driven, code-generation**, and so are parts of Result (a `partial` error gets its `WriteExtensions`) and Temporal (the property attributes such as `[AsUtc]`, and `[FromClock]`).

## When to use

You are handling operation outcomes, guards, validation, DTO mapping, dates, reusable predicates, or partial-update DTOs. For caching: `pragmatic-use-caching`. For i18n/money: start from `pragmatic-choose-modules`.

## Result — outcomes without exceptions

`Pragmatic.Result` — namespace `Pragmatic.Result`. Use it for **expected business failures**; exceptions remain for bugs.

**Use the built-in HTTP errors first** (namespace `Pragmatic.Result.Http`) — do not redefine them:

| Error | Status | Factories / properties |
|---|---|---|
| `NotFoundError` | 404 | `Create("User", id)` / `For<TId>("User", id)` — `EntityType`, `EntityId` |
| `BadRequestError` | 400 | `Create(reason)`, `InvalidParameter(name, reason)`, `MissingHeader`, `MalformedJson` |
| `UnauthorizedError` | 401 | `MissingToken()`, `InvalidToken()`, `ExpiredToken()`, `InvalidCredentials()` |
| `ForbiddenError` | 403 | `Create(resource, action)`, `MissingPermission(permission)`, `MissingPermissions(permissions, PermissionMatch.All/Any)`, `ActionDenied(action)` — wire: `requiredPermissions` array + `permissionMatch` |
| `ConflictError` | 409 | `AlreadyExists(entityType, id)`, `ConcurrencyConflict(...)`, `DuplicateKey(field)` |
| `BusinessRuleError` | 422 | `Create(rule, details)`, `InsufficientFunds`, `LimitExceeded`, `Inactive` |
| `InternalServerError` | 500 | `From(ex, includeDetails: false)`, `Create(message)` |
| `DependencyError` | 502/503/504 | `Unavailable(service, retryAfter)`, `Timeout(service)` — `IsTransient = true` |

```csharp
using Pragmatic.Result;
using Pragmatic.Result.Http;

Result<User, NotFoundError> Find(Guid id)
{
    var user = _repo.Get(id);
    if (user is null) return NotFoundError.For("User", id);   // implicit conversion → Failure
    return user;                                              // implicit conversion → Success
}

// Consumption
var label = result.Match(u => u.Name, e => e.Title);
var dto   = result.Map(u => u.ToDto());                  // transforms the success value
var next  = result.Bind(u => Validate(u));               // chains Result
```

**Custom error** — only when no built-in fits, with a domain-specific name:

```csharp
public sealed partial record RoomUnavailableError : Error
{
    public override string Code => "ROOM_UNAVAILABLE";       // UPPER_SNAKE_CASE
    public override int StatusCode => 409;
    public Guid RoomId { get; init; }
    public DateOnly Date { get; init; }
}
```

Declare it `partial`: the source generator then emits the `WriteExtensions` override (custom properties → ProblemDetails extensions, zero reflection). On a non-partial error, override `WriteExtensions(IDictionary<string, object?>)` by hand if the properties must reach the wire. Localization is driven by `Code` (via `MessageKey`) and `IErrorMessageResolver` — there is no generated `TitleKey`/`DescriptionKey`.

- `Result<T,E>` — success/failure union; `Value`/`Error` throw if you access the wrong side, use `IsSuccess`/`TryGetValue`.
- **Return the value or the error, not a wrapper.** `Result<T>` converts implicitly from both sides, so
  `return dto;` and `return SomeError.Create(...);` are the whole idiom — `Result<IReadOnlyList<X>>.Failure(err)`
  spells out what the compiler already knows. The success direction gets discovered on its own; the
  failure direction does not, and the same method routinely uses one and not the other.
- `VoidResult<E>` — operation with no value; `VoidResult<E>.Success()` / `.Failure(error)`. `VoidResult` — without typed error.
- `Error` is a base `record`: override `Code` and `StatusCode` (abstract); `Title` is optional (non-null, empty by default — same default on `IError`).
- `Result.Try(op, errMapper)` / `TryAsync` to wrap throwing code; `Result.FromNullable(value, error)` (also lazy `() => error`). `Maybe<T>` for the optional with `.ToResult(error)`.
- Analyzer **PRAG0001** (in `Pragmatic.Result.Analyzers`) warns on `.Value` access not guarded by a success check. Respond with `TryGetValue(out var v)`, `Match`, or a guard (`if (result.IsSuccess)` / early-return on `IsFailure`) — never suppress it.

**ASP.NET Core wiring** (`Pragmatic.Result.AspNetCore`) — handlers return `Result<T,E>` directly, the filter converts to HTTP (success → 200/204, failure → RFC 7807 ProblemDetails):

```csharp
builder.Services.AddPragmaticResult();          // ProblemDetails factory + resolver
app.MapGroup("").WithResultHandling();          // Minimal API: filter on the whole group
// MVC: options.Filters.Add<ResultActionFilter>() in AddControllers
```

**A `Result` itself on the wire** — nothing in the framework serializes one: endpoints unwrap it, and the remote invoker has its own envelope. When the application sends a closed result type as JSON (a message, a cache entry), it declares it and the generator writes a typed, AOT-safe converter: `[assembly: JsonResultContract<Result<OrderDto, NotFoundError, ConflictError>>]` (namespace `Pragmatic.Result.Serialization`).

Without `WithResultHandling()` an endpoint returning `Result` serializes the raw struct and fails — always wire the filter. Opt-out per endpoint with `[SkipResultHandling]`. The default resolver leaves ProblemDetails `detail` empty; register `AddPragmaticResult<TResolver>()` with an `IErrorMessageResolver` to localize `title`/`detail` from `error.Code`.

## Ensure — guards for programming errors

`Pragmatic.Ensure` — namespace `Pragmatic.Ensure`. For invariants and preconditions (bugs), **not** for user input validation.

```csharp
using static Pragmatic.Ensure.Ensure;

ThrowIfNull(repository);
ThrowIfNullOrWhiteSpace(email);
ThrowIfNegativeOrZero(quantity);
ThrowIfEmpty(id);                                         // Guid
```

`ThrowIf*` families: null, string (`ThrowIfNullOrEmpty`, `ThrowIfLongerThan`, `ThrowIfNotEmail`...), numeric (`ThrowIfNegative`, `ThrowIfOutOfRange`...), collections (`ThrowIfEmpty`, `ThrowIfContainsDuplicate`...), dates (`ThrowIfInPast`/`ThrowIfInFuture`), enum (`ThrowIfNotDefined`). `Is*` variants as `bool` predicates without throwing. `paramName` is captured via `[CallerArgumentExpression]`.

**This is the default, and `PRAG0100` says so.** The analyzer ships inside `Pragmatic.Ensure`, so a
consuming project gets it with no extra reference. It reports the three shapes Ensure replaces:

```csharp
_name = name ?? throw new ArgumentNullException(nameof(name));   // → Ensure.ThrowIfNull(name)
if (x is null) throw new ArgumentNullException(nameof(x));       // → Ensure.ThrowIfNull(x)
if (string.IsNullOrEmpty(s)) throw new ArgumentException(…);     // → Ensure.ThrowIfNullOrWhiteSpace(s)
```

What it deliberately leaves alone: a `throw` that checks **state** or a domain rule (the test is the
exception type — `Argument*` only), and an `if` with an `else`, where rewriting as a guard would change
what the method does. Severity is **Info**: this is house style, not a defect, and a build that fails
on style is a build people learn to bypass.

⚠️ Ensure is for **programming errors**. A guard that a user's input can trigger belongs in Validation
below — turning bad input into an exception loses the field name and the 422.

⚠️ **Nor for a row that is not there.** `Ensure.ThrowIfNull(order)` after a lookup turns a 404 into a
500. And the Result-returning forms do not shorten it: `Check.NotNull(value, error)`
(`Pragmatic.Ensure.Result`) returns a `VoidResult` without the value, and its `[NotNullWhen]` does not
narrow a non-`bool` return; `Result.FromNullable(value, error)` gives the value but still needs the
`if (r.IsFailure) return …` pair in an `Execute` written as statements. In an operation the lookup is
not written at all: `[LoadEntity<T>]` (`pragmatic-use-actions-endpoints`). The one right use in an
operation is an invariant another part guarantees — `Ensure.ThrowIfNull(Hours)` after a validator
refused the request without hours.

## Validation — input validation

`Pragmatic.Validation` — namespace `Pragmatic.Validation`. The SG generates **sync** validation on any `partial` class/record whose properties have validation attributes or are `required`.

```csharp
public partial class CreateUserRequest
{
    [Required, Email]            public required string Email { get; init; }
    [MinLength(2), MaxLength(80)] public required string Name { get; init; }
    [Range(18, 120)]             public int Age { get; init; }
}
```

Attributes (namespace `Pragmatic.Validation.Attributes`): `[Required]`, `[Email]`, `[Phone]`, `[Url]`, `[Regex]`, `[MinLength]`/`[MaxLength]`/`[Length]`, `[Range]`, `[GreaterThan]`/`[LessThan]`, `[GreaterThanProperty("Other")]`, `[NotEmpty]`, `[Count]`, `[FutureDate]`/`[PastDate]`, `[OneOf]`, `[RequiredIf("Prop", value)]`, `[ValidateElements]`.

And these, same namespace, which the list above left out — check here before writing a rule by hand:

| | |
|---|---|
| `[Positive]` `[Negative]` | sign of a number |
| `[GreaterThanOrEqual]` `[LessThanOrEqual]` | inclusive bounds, next to the exclusive `[GreaterThan]`/`[LessThan]` |
| `[LessThanProperty("Other")]` | the mirror of `[GreaterThanProperty]` |
| `[GreaterThanOrEqualProperty("Other")]` `[LessThanOrEqualProperty("Other")]` | the inclusive forms — a period whose end may be its start: `[GreaterThanOrEqualProperty(nameof(From))] To`. Do not check `To < From` by hand in `Execute` |
| `[EqualTo("Other")]` `[NotEqualTo("Other")]` | equal / different from another property (a confirmation field) |
| `[MinCount]` `[MaxCount]` | size of a collection, when `[Count]` is not the exact shape |
| `[NotWhiteSpace]` | non-empty *and* not only spaces |
| `[ValidEnum]` | the value is a declared member, not just any number cast to the enum |
| `[CreditCard]` | format check (Luhn). A national format such as an Italian *codice fiscale* is a custom `ValidationAttribute` of your own |
| `[RequiredIfNot("Prop", value)]` | the negative twin of `[RequiredIf]` |

**A bound that moves with the clock is a sync rule.** `[PastDate]`/`[FutureDate]` compare against
`ValidationTimeProvider.Current`, and a rule of your own does the same — "a year not in the future":

```csharp
public sealed class NotFutureYearAttribute : ValidationAttribute
{
    public override string DefaultMessageKey => "validation.not_future_year";

    public override bool IsValid(object? value)
        => value is not int year || year <= ValidationTimeProvider.Current.GetUtcNow().Year;
}
```

The generator instantiates it and calls `IsValid`, and reports its `DefaultMessageKey`. An async
validator is for a rule that needs a lookup (the database, another service), not for one that needs
the date.

**The message of a rule is a translation key — name it through its constant.** With
`translations/*.json`, every attribute's `MessageKey` takes the generated `TKeys` constant:
`[GreaterThanOrEqualProperty(nameof(From), MessageKey = TKeys.Validation.LeaveRequest.EndsBeforeItStarts)]`.
The generator reads the key although it writes the constant itself; one it cannot read is
**PRAG0222** (Warning), and the rule would report its default key.

**Async** validation (e.g. DB lookup) — dedicated class with `[Validator]`. ⚠️ Not for a row the
operation already loads with `[LoadEntity]`: that rule is `ValidateLoaded()` on the operation, run right
after the preload (see `pragmatic-use-actions-endpoints`), and a validator would read the row twice.

```csharp
[Validator]
public sealed class CreateReservationValidator(IReadRepository<Reservation> reservations)
    : IAsyncValidator<CreateReservationRequest>
{
    // One guest, one stay at a time: rows the operation does not load, which is what a validator is for.
    // "The room type exists" is NOT here — that is [LoadEntity<RoomType>("Request.RoomTypeId")] on the
    // action, a 404 from the invoker; and "the room is free" is its ValidateLoadedAsync, on the row
    // already read — here, both would read the room type a second time.
    public async Task<ValidationError> ValidateAsync(CreateReservationRequest r, CancellationToken ct = default)
    {
        var (guest, from, to) = (r.GuestId, r.CheckIn, r.CheckOut);

        var alreadyStaying = ReservationSpecifications.IsActive()
                             & ReservationSpecifications.ForGuest(guest)
                             & Spec<Reservation>.Where(x => x.CheckIn < to && x.CheckOut > from);

        return await reservations.ExistsAsync(alreadyStaying, ct)
            ? ValidationError.For(nameof(CreateReservationRequest.GuestId), "validation.guest.already_booked")
            : ValidationError.Valid;
    }
}
```

`ValidationError` is a struct (namespace `Pragmatic.Validation.Types`): it is itself an `IError`
(HTTP 422 — 400 is for a request that could not be read at all); `ValidationError.Valid` = success; `ValidationError.For(propertyPath, messageKey, …)`
builds a failure that names the offending field on the wire. `[Validator]` is enough on its own,
for a mutation and for a domain action alike: the generator sees a `[Validator]` implementing
`IAsyncValidator<T>` for the operation `T` and wires the call, with nothing else to write.
`[Validate]` on the operation (namespace `Pragmatic.Actions.Attributes`) only *changes* the default —
`Async = false` to switch it off, `AsyncOnly` to skip the sync pass. ⚠️ Declare the validator in the
**same assembly** as the operation: one declared elsewhere is invisible to the generator that decides,
and is reported (**PRAG0215**) rather than silently never called.

**`[AsyncValidate<TValidator>]` — run the expensive validator only when it matters.** Not a synonym
for `[Validate]`: it binds one validator to one property, and the composite skips it when that
property was not modified.

```csharp
public partial class Customer : Entity
{
    // The uniqueness check hits the database — it fires only when Email actually changes.
    [AsyncValidate<EmailUniquenessValidator>]
    public string Email { get; private set; } = "";
}
```

On a **class** it fires on any modification; on a **property**, only on that one. In create mode
(nothing yet modified) every bound validator runs. Reach for it when a validator costs a round trip
and the update usually touches other fields.

## Mapping — DTOs and projection expressions

`Pragmatic.Mapping` — namespace `Pragmatic.Mapping.Attributes`.

```csharp
[MapFrom<Amenity>]
[GenerateProjection]                                      // → Expression for SQL-translatable .Select()
[GenerateBodyOnlyVariant]                                 // → FromEntityBodyOnly (scalars only)
public partial record AmenityDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";

    [MapProperty(nameof(Amenity.Category), Format = "G")]
    public string CategoryName { get; init; } = "";

    [MapIgnore] public string Computed { get; init; } = "";
}

// Consumption
var dto  = AmenityDto.FromEntity(amenity);
var dtos = db.Amenities.Select(AmenityDto.Projection).ToList();    // SQL projection
```

`[MapFrom<T>]` generates `FromEntity` + `entity.ToXDto()` + collection overloads. `[MapTo<T>]` generates `ToEntity()`. Others: `[MapProperty("path", Separator=, Format=, Default=, Target=)]`, `[MapConverter<TConverter>]`, `[MapConstructor]`.

### Conversions the generator works out on its own

A property whose types differ on the two sides is converted without being told how: `string` ↔ number, `string` → `Guid`, `bool`, `DateTime`, `DateOnly`, `TimeOnly`, enum, and the reverse. `[MapConverter<T>]` is for what the generator cannot infer, not for these.

In a `[Query]` (the DTO's `Projection`), a `Format` or a `[MapConverter<T>]` over a column is computed on the client after the read and equals `FromEntity`; through a navigation that may be null it is left at its default (**PRAG0320**/**PRAG0321** — do not suppress them, they are the only sign). Such a member cannot be filtered or sorted on: filter the entity.

On a **write** path a value that cannot be converted — `"sette"` where a number is expected — is refused with a validation error naming the property (422), not a 500. The check is generated next to the conversion; you declare nothing.

- `[MapDerived<TDerivedSource, TDerivedDto>]` on a base DTO — polymorphic `FromEntity`: a `Dog` maps to `DogDto` through `AnimalDto.FromEntity`. The derived DTO carries its own `[MapFrom<Dog>]` and inherits the base DTO (**PRAG0330** otherwise); list the most-derived first, dispatch follows declaration order. Not honoured in an EF projection (**PRAG0331**).
- `[MapEnum(OnUnknown = UnknownEnumValue.X)]` — what happens to a name the enum does not have, and `EnumMatch.ByValue` to pair two enums by number instead of by name.
- `[ReferenceStrategy(ReferenceStrategy.Detach)]` — on a single navigation, whether a `null` in the payload **removes the link** or leaves it alone. The default leaves it: silence never destroys.
- `[LinkIds]` — a list of ids chooses which rows a collection navigation points at. Removing an id detaches that row, it does not delete it.

## Temporal — clock and dates

`Pragmatic.Temporal` — the host registers its services when the package is referenced. Inject `IClock` instead of `DateTimeOffset.UtcNow` (testable with a fake clock). Its property attributes (`[AsUtc]`, `[KeepTimezone]`, `[FromClientTimezone]`, …) and `[FromClock]` are read by the generator: see **`../pragmatic-ecosystem/references/api-surface/temporal.md`**.

```csharp
public sealed class ExpiryService(IClock clock, ITemporalCalculator calc)
{
    public bool IsExpired(LocalDate due) => calc.AddBusinessDays(clock.Today, 3) > due;
}
```

`IClock`: `UtcNow`, `Now`, `UtcToday`, `Today` (the two dates are `DateOnly`), `GetTimeProvider()`. Value types: `LocalDate`, `LocalTime`, `LocalDateTime`, `ZonedDateTime`, `DateRange`, `CronExpression`. `ITemporalCalculator`: business days, public holidays by `countryCode`, period navigation. Config: timezones via `services.AddPragmaticTemporal(o => o.DefaultTimeZone = TimeZoneResolver.GetTimeZone("Europe/Rome"))`; holiday provider and clock via `app.UseTemporal(t => t.UseHolidayProvider<MyHolidays>())`.

## Specification — reusable predicates

`Pragmatic.Specification` — namespace `Pragmatic.Specification`. Encapsulates a composable `Expression<Func<T,bool>>`, usable both in-memory and translated to SQL.

```csharp
public sealed class ActiveUserSpec : Specification<User>
{
    public override Expression<Func<User, bool>> ToExpression()
        => u => u.IsActive && !u.IsDeleted;
}

var spec  = Spec<User>.Where(u => u.Role == "Admin").And(new ActiveUserSpec());
var users = await db.Users.Where(spec).ToListAsync();     // translated to SQL
bool ok   = spec.IsSatisfiedBy(user);                     // evaluated in memory
```

Composition: `.And()`, `.Or()`, `.Not()`, `.AndIf(condition, spec)`, operators `&` `|` `!`. `Spec<T>.True`/`False`/`Where(predicate)`.

### Where to put them: the generated container

Every `[Entity]` gets a generated `{Entity}Specifications` — `ById(id)`, and `By{LogicKey}(key)` when the entity declares one — as both a `Specification<T>` and an `IQueryable<T>` extension. The class is **`partial`**, so put your own specifications in it rather than in a class of their own:

```csharp
// generated, in the entity's namespace
public static partial class GuestSpecifications
{
    public static Specification<Guest> ById(Guid id) => ...;
}

// yours — same name, same namespace, same file layout as any other partial
public static partial class GuestSpecifications
{
    public static Specification<Guest> Active()
        => Spec<Guest>.Where(g => g.IsActive && !g.IsDeleted);

    public static Specification<Guest> InCity(string city)
        => Spec<Guest>.Where(g => g.City == city);
}
```

One name to remember, and `ById` composes with yours without an extra `using`. A specification earns a name when the same predicate is asked from more than one place, or when the predicate **is** a rule rather than a filter — written out as a lambda in each caller, two copies drift and nothing compares them.

### Publishing one as a read

A named rule does not need a second class restating it. `[Query]` on a **static** member returning `Specification<T>` derives `{Member}Query` beside it — the member's parameters become the query's inputs, and `[Endpoint]`/`[RequirePermission]` written next to it apply to the derived query:

```csharp
[Query<Guest>(Paged = true)]
[Endpoint(HttpVerb.Get, "api/guests/active")]
public static Specification<Guest> Active()
    => Spec<Guest>.Where(g => g.IsActive && !g.IsDeleted);
```

The specification is **not** modified: it stays a predicate and still composes with `&`, which is what makes it worth naming. The route is opt-in — without `[Endpoint]` the query exists in process only. Full rules, and when to use a `[Query]` class instead, in `pragmatic-use-persistence`.

### Loading an operation's input by one

An action or a mutation that needs the rows a rule matches names it on its load instead of injecting a repository: `[LoadEntity<Guest>(Specification = nameof(GuestSpecifications.InCity))]` (first row, 404 when none) or `[LoadEntities<Guest>(Specification = …)]` (every row; `RequireAny = true` makes none a 404). The member's parameters bind **by name** to the operation's properties — so name them as the operation names its properties. Details in `pragmatic-use-actions-endpoints`.

## Patch — partial update (two distinct attributes)

There are **two** patch mechanisms in separate packages. Choose based on the use case:

| | `[GeneratePatch<T>]` | `[Patch<T>]` |
|---|---|---|
| Package | `Pragmatic.Patch` | `Pragmatic.Persistence` |
| Namespace | `Pragmatic.Patch.Attributes` | `Pragmatic.Persistence.Patch` |
| Tri-state | Yes — `Optional<T>` distinguishes *unset* / *explicit null* / *value* | No — uses ordinary nullable properties |
| Tracking | `ModifiedProperties` (auto from `HasValue`) | `SetProperties` + `MarkSet(name)` |
| Apply | `ApplyTo(entity)` | `ApplyPatch(entity)` |
| JSON converter | Generated | Not generated |
| Use for | **HTTP PATCH endpoint** (absent field ≠ null field) | Internal patch / command |

```csharp
// HTTP PATCH: need to distinguish "field not sent" from "field = null"
[GeneratePatch<Amenity>]
public partial record PatchAmenityDto;
// generates: Optional<string> Name { get; init; }  +  void ApplyTo(Amenity)  +  ModifiedProperties

patch.ApplyTo(amenity);                                   // applies only properties with HasValue
```

## Build verification

```powershell
dotnet build                                              # PRAG02xx Validation, PRAG03xx Mapping, PRAG22xx Patch
dotnet test path\to\App.Tests --no-restore -v minimal
```

If generated members are missing (`FromEntity`, `ApplyTo`, validation): `Pragmatic.SourceGenerator` must be referenced as an analyzer with `<IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>`.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing and Showcase example
applications — code that compiles and that `Invoicing.IntegrationTests` and `Showcase.IntegrationTests`
exercise — and kept identical to it by the gate: a typed error with its parameters, an action answering
`Result<T, IError>`, validation on an input, DTOs with mapping and projection, specifications in their
container, and an HTTP PATCH with `[GeneratePatch<T>]`.
