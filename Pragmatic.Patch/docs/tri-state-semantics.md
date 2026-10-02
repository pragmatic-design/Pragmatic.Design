# Tri-State Semantics and Optional<T>

HTTP PATCH operations need to distinguish three states: a field was **not sent** (leave unchanged), **sent as null** (clear the value), or **sent with a value** (update). Standard C# nullability cannot express "not sent" vs "null". `Optional<T>` solves this with a tri-state struct.

---

## The Problem

Consider a PATCH request to update a product:

```json
{ "name": "New Name" }
```

This should update `Name` but leave `Description` unchanged. But with a normal DTO:

```csharp
public record UpdateProduct
{
    public string? Name { get; init; }        // "New Name"
    public string? Description { get; init; } // null — but does this mean "clear" or "not sent"?
}
```

There is no way to tell if `Description` was explicitly set to `null` or simply absent from the JSON.

---

## Optional<T>

`Optional<T>` is a readonly struct with three states:

| State | `HasValue` | `IsUndefined` | `Value` | Meaning |
|-------|-----------|---------------|---------|---------|
| **Undefined** | `false` | `true` | throws | Field was not in the JSON payload |
| **Null** | `true` | `false` | `null` | Field was explicitly set to `null` |
| **Value** | `true` | `false` | the value | Field was set to a specific value |

### Factory Methods

```csharp
Optional<string>.Undefined          // Not sent
Optional<string>.Null               // Sent as null
Optional<string>.Of("Alice")        // Sent with value
Optional<string>.Of(null)           // Same as Null

// Implicit conversion from T
Optional<string> name = "Alice";    // Of("Alice")
```

### Querying

```csharp
var name = Optional<string>.Of("Alice");

if (name.HasValue)
    Console.WriteLine(name.Value);  // "Alice"

// Safe access with default
string display = name.GetValueOrDefault("Unknown");

// Functional style
name.IfPresent(n => Console.WriteLine(n));

// Transform
Optional<int> length = name.Map(n => n?.Length ?? 0);
```

---

## JSON Serialization

The generated converter of a patch type (and, for a hand-written DTO, `OptionalConverter<T>`) handles JSON deserialization with correct tri-state mapping:

| JSON | Deserialized to |
|------|----------------|
| `{ "name": "Alice" }` | `Name = Optional.Of("Alice")`, `Email = Optional.Undefined` |
| `{ "name": null }` | `Name = Optional.Null` |
| `{ }` | `Name = Optional.Undefined`, `Email = Optional.Undefined` |

The converter is registered automatically on SG-generated patch types via `[JsonConverter]` attribute.

### Value types and explicit `null`

The table above assumes a reference type such as `string`, where "clear it" is a state the property can
actually hold. For a **non-nullable value type** — `int`, `DateOnly`, an enum — there is no such state:
`Optional<int>.Null` carries `HasValue = true` with the value `default(int)`, i.e. `0`. Left alone, an
incoming `null` would therefore *write zero* rather than leave the field untouched.

The two converters resolve this differently — not by accident, but because they see different things.
The generated one reads the whole patch object, so it knows which properties were absent. The runtime one
is registered per value: it is handed a token and cannot tell `{"quantity": null}` from `{}`, so "null" is
the only signal it has.

| Converter | `{ "quantity": null }` on `Optional<int>` | When it applies |
|-----------|-------------------------------------------|-----------------|
| SG-generated `{Type}JsonConverter` | treated as **undefined** — the field is left unchanged | any type marked `[GeneratePatch<T>]` (the normal case) |
| Runtime `OptionalConverter<T>` | becomes `Optional.Null`, so `ApplyTo` writes `default(T)` | only when you register the converter by hand on a type the generator did not produce |

Practical consequence: to make a value-type field optional *and* clearable, model it as
`Optional<int?>` rather than `Optional<int>`. Then `null` is a value the property can genuinely hold and
both converters agree. If you are hand-registering `OptionalConverter<T>` on a non-generated type,
be aware that a client sending `null` for a non-nullable value type will reset it to the default.

---

## Source-Generated Patch Types

Mark a record with `[GeneratePatch<TEntity>]` and the SG generates a complete patch DTO:

```csharp
[GeneratePatch<Product>]
public partial record PatchProduct;
```

### Generated Code

```csharp
public partial record PatchProduct
{
    [JsonConverter(typeof(OptionalConverter<string>))]
    public Optional<string> Name { get; init; }

    [JsonConverter(typeof(OptionalConverter<string?>))]
    public Optional<string?> Description { get; init; }

    [JsonConverter(typeof(OptionalConverter<decimal>))]
    public Optional<decimal> Price { get; init; }

    public void ApplyTo(Product entity)
    {
        if (Name.HasValue) entity.SetName(Name.Value);
        if (Description.HasValue) entity.SetDescription(Description.Value);
        if (Price.HasValue) entity.SetPrice(Price.Value);
    }

    public IReadOnlySet<string> ModifiedProperties => /* tracks which properties were in the JSON */;
}
```

### Property Exclusion

The generator automatically excludes:
- `Id`, `PersistenceId` — identity cannot change
- `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` — managed by auditing
- `IsDeleted`, `DeletedAt`, `DeletedBy` — managed by soft delete
- `RowVersion` — managed by concurrency
- Collection navigations — not patchable via simple PATCH
- Reference navigations — use explicit FK properties instead

---

## Using Patch in Endpoints

```csharp
[Endpoint(HttpVerb.Patch, "/api/products/{productId}")]
public partial class PatchProductEndpoint : Endpoint<ProductDto>
{
    private IRepository<Product> _products;
    private IUnitOfWork _uow;

    [FromRoute] public required Guid ProductId { get; init; }
    [FromBody] public required PatchProduct Patch { get; init; }

    public override async Task<Result<ProductDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(ProductId, ct);
        if (product is null)
            return NotFoundError.For("Product", ProductId.ToString());

        Patch.ApplyTo(product);
        await _uow.SaveChangesAsync(ct);

        return ProductDto.FromEntity(product);
    }
}
```

### Request Examples

```http
### Update only the name
PATCH /api/products/abc123
Content-Type: application/json

{ "name": "Updated Product" }

### Clear the description
PATCH /api/products/abc123
Content-Type: application/json

{ "name": "Updated", "description": null }

### No changes (valid but no-op)
PATCH /api/products/abc123
Content-Type: application/json

{ }
```

---

## Integration with Change Tracking

When `ApplyTo()` calls the entity's `Set*()` methods, `IChangeTracking.ModifiedProperties` is updated automatically. This means:

- **Selective validation** — only modified properties are validated
- **Optimized persistence** — EF Core tracks only changed columns
- **Audit trail** — `EntityPropertyChanged<T>` events fire only for actual changes

---

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| `PRAG2200` | Error | `[GeneratePatch<T>]` type must be `partial` |
| `PRAG2201` | Error | Entity type could not be resolved |
| `PRAG2202` | Warning | Entity has no settable properties (patch would be empty) |
