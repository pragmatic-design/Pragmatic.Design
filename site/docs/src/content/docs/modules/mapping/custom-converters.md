---
title: "Custom Converters Guide"
description: "This guide covers `IValueConverter<TSource, TTarget>` and `[MapConverter<T>]` for custom property type conversions."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/docs/custom-converters.md
sidebar:
  order: 4
---
This guide covers `IValueConverter<TSource, TTarget>` and `[MapConverter<T>]` for custom property type conversions.

## Overview

When the built-in type conversions (ToString, Parse, DateTime/DateOnly, etc.) are not sufficient, you can implement a custom converter. Converters are stateless, bidirectional, and instantiated by the generator with `new TConverter()`.

## Implementing IValueConverter

The `IValueConverter<TSource, TTarget>` interface has two methods:

```csharp
using Pragmatic.Mapping.Converters;

public interface IValueConverter<TSource, TTarget>
{
    TTarget Convert(TSource source);      // Entity -> DTO (used by [MapFrom])
    TSource ConvertBack(TTarget target);  // DTO -> Entity (used by [MapTo])
}
```

### Example: MoneyToStringConverter

From the Showcase app (`examples/showcase/src/Showcase.Billing/Infrastructure/Converters/MoneyToStringConverter.cs`):

```csharp
using Pragmatic.Mapping.Converters;

public sealed class MoneyToStringConverter : IValueConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("N2");

    public decimal ConvertBack(string target) =>
        decimal.TryParse(target, out var result) ? result : 0m;
}
```

### Example: Generic Enum-to-Int Converter

```csharp
public class EnumToIntConverter<TEnum> : IValueConverter<TEnum, int>
    where TEnum : struct, Enum
{
    public int Convert(TEnum source) => System.Convert.ToInt32(source);
    public TEnum ConvertBack(int target) => (TEnum)Enum.ToObject(typeof(TEnum), target);
}
```

## Using `[MapConverter<T>]`

Apply `[MapConverter<T>]` to a property to use your converter:

```csharp
using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Converters;

[MapFrom<Invoice>]
public partial class InvoiceSummaryDto
{
    public decimal TotalAmount { get; init; }

    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalFormatted { get; init; } = "";
}
```

The generator calls `new MoneyToStringConverter().Convert(entity.TotalAmount)` in the `FromEntity()` method.

### Combined with `[MapProperty]`

You can combine `[MapConverter<T>]` with `[MapProperty]` to specify both the source path and the converter:

```csharp
// Source property + custom converter
[MapProperty(nameof(Invoice.TotalAmount))]
[MapConverter<MoneyToStringConverter>]
public string TotalFormatted { get; init; } = "";

// Navigation path + custom converter
[MapProperty("Order.Total")]
[MapConverter<MoneyToStringConverter>]
public string OrderTotal { get; init; } = "";
```

### Class-level converters

`[MapConverter<T>]` can also be placed on the **DTO type**. It then applies to every convention-mapped scalar whose source→target types match the converter's `IValueConverter<TSource, TTarget>` signature — no need to repeat the attribute on each property. A property-level converter always wins over a class-level one.

```csharp
[MapFrom<Order>]
[MapConverter<MoneyToStringConverter>]   // decimal → string, applied to all matching props
public partial class OrderDto
{
    public string Total { get; init; } = "";      // entity.Total (decimal) → converted
    public string Shipping { get; init; } = "";    // entity.Shipping (decimal) → converted
    public string Reference { get; init; } = "";    // entity.Reference is string → converter skipped (types don't match)
}
```

Converter instances are cached once in a `static readonly` field per converter type — no per-call allocation, whether applied at the property or the class level.

## Requirements

1. **Parameterless constructor**: The converter must have a `public` parameterless constructor. The generator calls `new TConverter()` directly. Missing it reports **PRAG0306**.
2. **Stateless**: Converters should not hold mutable state.
3. **`class` constraint**: The `[MapConverter<TConverter>]` attribute requires `where TConverter : class, new()`. If the type does not implement `IValueConverter<,>`, the generator reports **PRAG0305**.

If these requirements are not met, the generator reports:

| Diagnostic | Description |
|------------|-------------|
| PRAG0305 | Converter does not implement `IValueConverter<TSource, TTarget>` |
| PRAG0306 | Converter does not have a parameterless constructor |

## Converter in `[MapTo]` and `ApplyTo()`

When the DTO also has `[MapTo<T>]`, the `ConvertBack` method is used:

```csharp
[MapFrom<Invoice>]
[MapTo<Invoice>]
public partial class InvoiceDto
{
    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalFormatted { get; init; } = "";
}

// MapFrom: entity.TotalAmount -> new MoneyToStringConverter().Convert(...)
// MapTo:   dto.TotalFormatted -> new MoneyToStringConverter().ConvertBack(...)
```

## In a Projection

SQL cannot run a converter, so the projection selects the source column and runs the converter on the
client after the read — the query's last step, where EF Core evaluates what it cannot translate. The value
is the one `FromEntity` gives. The projection calls it through a generated static method
(`ConvertAfterTheRead_{Property}`), because EF Core refuses a client projection that calls an instance
method on a cached object.

Through a navigation that may be null it cannot (`[MapProperty("Guest.Name")]` with `Guest` nullable): the
property is excluded from the projection and receives `default` (PRAG0320 warning).

```csharp
[MapFrom<Invoice>]
[GenerateProjection]
public partial class InvoiceSummaryDto
{
    public decimal TotalAmount { get; init; }         // Included in Projection

    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalFormatted { get; init; } = "";  // Computed on the client after the read
}
```

⚠️ The projection is the **last** step: `.Select(InvoiceSummaryDto.Projection).Where(d => d.TotalFormatted …)`
asks SQL for a value only the client computes, and EF Core refuses it at run time. Filter on the entity first.

## When to Use Converters vs Built-In Conversions

| Scenario | Approach |
|----------|----------|
| `int` to `string` | Built-in (automatic `.ToString()`) |
| `enum` to `string` | Built-in (automatic `.ToString()`) |
| `DateTime` to `DateOnly` | Built-in (automatic `DateOnly.FromDateTime()`) |
| Custom formatting (`decimal` to `"$1,234.56"`) | `[MapConverter<T>]` |
| Domain type to primitive (`Money` to `string`) | `[MapConverter<T>]` |
| Encryption/decryption | `[MapConverter<T>]` |
| Complex transformation (multiple fields) | `CustomizeMapping()` hook |

## Manual Property Mapping with `[MapProperty]`

For cases where you do not need a full converter but want explicit control over the source path:

```csharp
[MapFrom<Reservation>]
public partial class ReservationSummaryDto
{
    // Explicit navigation path
    [MapProperty("Guest.FirstName")]
    public string GuestFirstName { get; init; } = "";

    // Concatenation from multiple paths
    [MapProperty("Guest.FirstName", "Guest.LastName")]
    public string GuestFullName { get; init; } = "";

    // Custom separator
    [MapProperty("Guest.LastName", "Guest.FirstName", Separator = ", ")]
    public string GuestNameReversed { get; init; } = "";

    // Default for nullable to non-nullable
    [MapProperty(nameof(Reservation.Notes), Default = "")]
    public string Notes { get; init; } = "";
}
```

### Target Path for `[MapTo]`

When mapping back to an entity with nested properties, use the `Target` parameter:

```csharp
[MapTo<Order>]
public partial record UpdateOrderDto
{
    [MapProperty(Target = "Customer.Name")]
    public string CustomerName { get; init; } = "";

    [MapProperty(Target = "ShippingAddress.City")]
    public string ShippingCity { get; init; } = "";
}
```

This generates null-safe assignments — intermediate navigations are created (`??= new()`) when they have an accessible parameterless constructor, otherwise the assignment is null-guarded (skipped when the navigation is null). No `NullReferenceException`:

```csharp
entity.Customer ??= new();
entity.Customer.Name = this.CustomerName;

entity.ShippingAddress ??= new();
entity.ShippingAddress.City = this.ShippingCity;
```
