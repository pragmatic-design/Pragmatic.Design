---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Mapping. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/docs/troubleshooting.md
sidebar:
  order: 8
---
Practical problem/solution guide for Pragmatic.Mapping. Each section covers a common issue, the likely causes, and the fix.

---

## No Mapping Code Generated

You added `[MapFrom<T>]` or `[MapTo<T>]` but no `FromEntity()`, `ToEntity()`, or extension methods appear.

### Checklist

1. **Is the class `partial`?** The SG cannot generate into a non-partial class. Diagnostic PRAG0300 is emitted if this is missing.

2. **Is the analyzer present?** How you get it depends on how you consume Pragmatic.Mapping:

   **NuGet consumers (normal case).** Add the package: the source generator is delivered as an analyzer with it, so no extra wiring is needed:

   ```bash
   dotnet add package Pragmatic.Mapping
   ```

   ```xml
   <PackageReference Include="Pragmatic.Mapping" />
   ```

   For a working, package-based reference setup see the consumer samples under `examples/consumer-samples/`.

   **In-repo / contributor builds (this monorepo only).** When building against project sources rather than the package, reference the generator project directly as an analyzer:

   ```xml
   <!-- Repository-relative path; only valid inside the Pragmatic.Design monorepo -->
   <ProjectReference Include="..\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

   Without `OutputItemType="Analyzer"`, the generator is treated as a normal reference and does not run.

3. **Is `Pragmatic.Mapping` referenced?** The generator detects the mapping feature by looking for `Pragmatic.Mapping.Attributes.MapFromAttribute` in the compilation. Without the package reference, the feature is not activated.

4. **Rebuild the project.** Source generators run during compilation. After adding the attribute, a full rebuild (`dotnet build` or Ctrl+Shift+B) is needed. IntelliSense may lag behind until the next build.

5. **Check the generated files.** In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. The generated files should appear as `{Type}.Mapping.g.cs` and `{Type}.Extensions.g.cs`. On disk, they are in `obj/Debug/net10.0/generated/`.

---

## FromEntity Returns Unexpected Default Values

The mapping runs, but some properties on the DTO are `default("")`, `0`, or `null` instead of the expected values.

### Possible Causes

**Property name mismatch.** The DTO property name does not match any source property. The generator emits PRAG0303 for each unmatched property. Check the build warnings.

**Navigation property not loaded.** When using EF Core with `FromEntity()`, navigation properties must be explicitly loaded:

```csharp
// Guest is null because it was not included
var reservation = await db.Reservations.FindAsync(id);
var dto = ReservationDto.FromEntity(reservation);  // GuestFirstName = ""

// Fix: Include the navigation
var reservation = await db.Reservations
    .Include(r => r.Guest)
    .FirstOrDefaultAsync(r => r.PersistenceId == id);

// Or use Projection (recommended):
var dto = await db.Reservations
    .Where(r => r.PersistenceId == id)
    .Select(ReservationDto.Projection)
    .FirstOrDefaultAsync();
```

Check `ReservationDto.RequiredNavigations` to see which navigations the DTO needs.

**Converter property in projection.** If the DTO has `[GenerateProjection]` and a property uses `[MapConverter<T>]`, that property is excluded from the projection (PRAG0320) and gets `default`. Use `FromEntity()` instead, or restructure the DTO.

**Format string in projection.** Properties with `[MapProperty(Format = "...")]` are excluded from projections (PRAG0321) for the same reason.

---

## Projection Not Generating

The DTO has `[GenerateProjection]` but no `Projection` property appears.

### Checklist

1. **Does the DTO also have `[MapFrom<T>]`?** `[GenerateProjection]` requires `[MapFrom<T>]` on the same type. Without it, no projection is generated.

2. **Are all nested DTOs also decorated?** If the DTO has a property of another DTO type, that nested DTO must also have `[MapFrom<T>]`. Missing it produces PRAG0309.

3. **Check for circular references.** Circular DTO references are not supported in projections. The generator handles them in `FromEntity()` with instance tracking, but cannot express them in an Expression Tree.

---

## Type Conversion Errors

When a source property type cannot be assigned to the target property type and no conversion path exists, the generator reports **PRAG0304** (`incompatible types`) on the DTO: a clear, actionable error rather than a cryptic compiler error buried in generated code.

### Supported Automatic Conversions

The generator handles these conversions automatically:

| Direction | Types |
|-----------|-------|
| To `string` | `int`, `long`, `decimal`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `enum` |
| From `string` | `int`, `long`, `decimal`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `enum` |
| Date conversions | `DateTime` to/from `DateOnly`, `DateTime` to `TimeOnly` |
| Enum to enum | different enum types, mapped by member name (validated by PRAG0328) |
| Implicit widening | `int` to `long`, `int` to `decimal`, and every other C# implicit numeric conversion |

**Numeric narrowing is not automatic.** A lossy pair such as `long` → `int` or `double` → `float` reports **PRAG0304** (it is not an implicit C# conversion). Widen the DTO property, or add a converter.

If your conversion is not in this list, you need a custom converter:

```csharp
public sealed class MyConverter : IValueConverter<SourceType, TargetType>
{
    public TargetType Convert(SourceType source) => /* ... */;
    public SourceType ConvertBack(TargetType target) => /* ... */;
}

// Apply to the property:
[MapProperty(nameof(Entity.SourceProp))]
[MapConverter<MyConverter>]
public TargetType TargetProp { get; init; }
```

### Converter Validation

The generator validates the converter contract at compile time:

| Problem | Diagnostic | Fix |
|---------|-----------|-----|
| Converter does not implement `IValueConverter<TSource, TTarget>` | **PRAG0305** (Error) | Implement the interface with the correct type parameters |
| Converter has no public parameterless constructor | **PRAG0306** (Error) | Add a `public` parameterless constructor |

---

## Nested DTO Mapping Errors

### PRAG0309: Nested type missing [MapFrom]

The DTO has a property whose type is another DTO, but that nested DTO does not have `[MapFrom<T>]`.

```csharp
// Fix: Add [MapFrom] to the nested DTO
[MapFrom<Address>]
public partial class AddressDto { /* ... */ }
```

### Nested DTO property mismatch

If the nested DTO has `[MapFrom<X>]` but the source navigation property is of an unrelated type `Y`, the generator reports **PRAG0315** (`nested DTO property mismatch`).

### Nested projection type missing [GenerateProjection]

When the parent DTO has `[GenerateProjection]` and a nested DTO property, the nested DTO must also support projection. The generator inlines the nested projection into the parent expression.

---

## Ambiguous Mapping

A target property can match a source by more than one strategy: for example, a direct name match *and* a flattening convention, when the entity has both a `Status` property and a navigation `Status` with further properties.

The generator resolves the match deterministically by priority: an explicit `[MapProperty]` wins first, then a direct name match, then the flattening convention, then concatenation. When a direct name match *and* a flattening convention both apply, it emits **PRAG0323** (`ambiguous mapping`) so the choice is visible. If the automatically chosen source is not the one you want, make it explicit.

**Fix:** Add `[MapProperty("Status")]` or `[MapProperty("Status.Name")]` to pin the source.

---

## Generated Code Not Compiling

The build fails with PRAG03xx diagnostics from the source generator.

### Diagnostics Reference

The complete set of diagnostics the generator emits. IDs 0301, 0308, 0311, 0312 and 0318 are retired and not reused, so the numbering has intentional gaps.

**Errors** (fail the build):

| ID | Cause | Fix |
|----|-------|-----|
| PRAG0300 | Type is not `partial` | Add `partial` to the type declaration |
| PRAG0302 | `[MapProperty]` source path or `Target` segment not found | Correct the path; every segment must exist on the type |
| PRAG0304 | Incompatible types, no conversion path (e.g. `Guid`→`bool`, numeric narrowing) | Widen the DTO type or add a `[MapConverter]` |
| PRAG0305 | Converter does not implement `IValueConverter<,>` | Implement the interface with the right type args |
| PRAG0306 | Converter has no public parameterless constructor | Add one |
| PRAG0309 | Nested DTO missing `[MapFrom]` | Add `[MapFrom<T>]` to the nested DTO |
| PRAG0310 | `[GenerateProjection]` without `[MapFrom]` | Add `[MapFrom<T>]` |
| PRAG0314 | `[MapIgnore]` and `[MapProperty]` on the same property | Remove one (ignore wins) |
| PRAG0315 | Nested DTO `[MapFrom<T>]` unrelated to the navigation type | Fix the generic argument |
| PRAG0316 | No suitable `[MapTo]` constructor | Add a parameterless ctor or `[MapConstructor]` |
| PRAG0317 | Nullable → non-nullable (non-simple target) without `Default` | Add `[MapProperty(Default = …)]` |
| PRAG0328 | Enum→enum: a source member has no same-named target member | Add the member or use a converter |
| PRAG0329 | `[MapCondition]` predicate missing or wrong shape | Make it a `static bool` method taking the source type |
| PRAG0330 | Invalid `[MapDerived]` pair (inheritance contract) | Derived source must derive the `[MapFrom]` source; derived DTO must derive the base DTO |

**Warnings:**

| ID | Cause | Fix |
|----|-------|-----|
| PRAG0303 | No matching source property (uses `default`) | Fix the name, add `[MapProperty]`, or add `[MapIgnore]` |
| PRAG0307 | Required `[MapTo]` property not mapped | Add the property or make it optional on the entity |
| PRAG0319 | `CustomizeMapping` ignored in projection | Accept that it runs only in `FromEntity`/`Selector` |
| PRAG0320 | `[MapConverter]` excluded from projection | Use the raw type in the projection DTO or accept `default` |
| PRAG0323 | Direct match *and* flattening convention both apply | Add `[MapProperty]` to pin the source |
| PRAG0326 | Nested projection drops members it cannot inline (converter, format, unresolved path, or a source that is neither a column nor a nested DTO) | Project the nested DTO via its own `.Projection`, or map those members another way |
| PRAG0327 | Nested projection truncated by `MaxDepth` | Raise `[GenerateProjection(MaxDepth = …)]` |

**Info / Hidden:**

| ID | Severity | Meaning |
|----|----------|---------|
| PRAG0313 | Info | Circular reference detected; instance tracking used (no action) |
| PRAG0321 | Info | Format string excluded from projection |
| PRAG0322 | Info | Complex dictionary value not supported in projection |
| PRAG0324 | Info | ID excluded from `ToEntity()`; add `[MapProperty]` to include |
| PRAG0331 | Info | `[MapDerived]` not honored by projections (base shape only) |
| PRAG0332 | Info | `[MapCondition]` with a block body gates `FromEntity` only; the projection maps unconditionally. Give the predicate an expression body to gate the projection too |
| PRAG0325 | Hidden | Source property not mapped to the DTO (its data is dropped); raise to Warning via `.editorconfig` for reverse-coverage checks |

Check the **Error List** window in Visual Studio or the build output for diagnostic details and the affected source location.

---

## EF Core Projection Issues

### Query returns all columns instead of projected subset

Verify you are using `Projection` (Expression), not `Selector` (Func):

```csharp
// Wrong: Selector is a Func -- EF Core evaluates client-side
db.Guests.Select(GuestDto.Selector).ToListAsync();

// Right: Projection is an Expression -- EF Core translates to SQL
db.Guests.Select(GuestDto.Projection).ToListAsync();
```

### Projection compiles but throws at runtime

EF Core throws `InvalidOperationException` with "could not be translated" message. This means the expression contains a method call that EF Core's SQL translator does not support. Check for:

- Custom converters (should have been excluded with PRAG0320)
- String methods that EF Core does not translate
- Complex LINQ operations in nested collections

Solution: simplify the DTO to use only SQL-translatable property assignments.

---

## MapTo Constructor Issues

### PRAG0316: No suitable constructor found

The target entity does not have a constructor that the generator can use.

```csharp
// Entity with no parameterless constructor:
public class User
{
    public User(string email) { Email = email; }
    public string Email { get; }
    public string Name { get; init; } = "";
}

// Fix: Add [MapConstructor] to the preferred constructor
public class User
{
    public User() { }  // Or add parameterless

    [MapConstructor]
    public User(string email) { Email = email; }
    public string Email { get; }
    public string Name { get; init; } = "";
}
```

The generator's constructor selection algorithm:
1. Use the constructor marked with `[MapConstructor]`
2. Use the parameterless constructor if available
3. Use the best-match constructor (most parameters matched by DTO properties)

---

## FAQ

### Can I map a `record struct` with `[MapFrom]`?

Yes. `[MapFrom<T>]` supports `class`, `record`, `struct`, and `record struct`. The type must be `partial`. `[MapTo<T>]` and `[GenerateProjection]` for structs are planned but not yet implemented.

### Do I need DI registration for mapping?

No. All generated mapping code is static. No DI registration, no service injection, no startup configuration. Just reference the package and use the generated methods.

### Can I map from multiple source types?

Not with multiple `[MapFrom]` attributes on the same class -- this causes a compiler error (duplicate attribute). Create separate DTO types for each source type:

```csharp
[MapFrom<Guest>]
public partial class GuestDto { /* ... */ }

[MapFrom<Employee>]
public partial class EmployeeDto { /* ... */ }
```

### What happens to properties that exist on the DTO but not the entity?

They receive `default` (e.g., `""` for string, `0` for int, `null` for nullable types) and the generator emits PRAG0303. Use `[MapIgnore]` to suppress the warning for intentionally unmapped properties like computed fields.

### Can I debug the generated mapping code?

Yes. The generated files are in `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/`. You can open them in your IDE and set breakpoints. In Visual Studio, they are also visible under **Dependencies > Analyzers > Pragmatic.SourceGenerator**.

### How do I handle inheritance?

The generator maps inherited properties from base classes automatically. If `Guest` inherits from `BaseEntity` which has `Id` and `CreatedAt`, those properties are available for mapping on `[MapFrom<Guest>]` DTOs.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working mapping implementations across entities.
- **Projections Guide**: See [projections.md](/modules/mapping/projections/) for EF Core projection details.
- **Custom Converters**: See [custom-converters.md](/modules/mapping/custom-converters/) for `IValueConverter` reference.
- **Feature Matrix**: See [feature-matrix.md](/modules/mapping/feature-matrix/) for complete feature support status.
- **Concepts**: See [concepts.md](/modules/mapping/concepts/) for architecture overview and decision guide.
