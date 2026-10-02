using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates automatic type conversion features in Pragmatic.Mapping.
///     Shows P1 (ToString/Parse), P2 (DateTime conversions), P3 (Dictionary DTOs).
/// </summary>
public static class TypeConversionSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Type Conversion Samples");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // P1: ToString/Parse conversions
        ShowToStringConversions();
        ShowParseConversions();

        // P2: DateTime conversions
        ShowDateTimeConversions();

        // P3: Dictionary with DTO values
        ShowDictionaryDtoMapping();

        // P3: Inheritance mapping
        ShowInheritanceMapping();

        Console.WriteLine();
    }

    private static void ShowToStringConversions()
    {
        Console.WriteLine("  6.1 ToString Conversions (int/enum/Guid/DateTime -> string)");
        Console.WriteLine("  -----------------------------------------------------------");

        var product = new ProductWithNumericFields
        {
            Id = Guid.NewGuid(),
            Quantity = 42,
            Price = 99.99m,
            IsAvailable = true,
            Status = ProductStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        // Auto-converts all fields to string
        var dto = StringConversionsDto.FromEntity(product);

        Console.WriteLine($"    Original: Id={product.Id}, Qty={product.Quantity}, Price={product.Price}");
        Console.WriteLine($"    DTO:      Id={dto.Id}, Qty={dto.Quantity}, Price={dto.Price}");
        Console.WriteLine($"    Status:   {dto.Status} (was enum {product.Status})");
        Console.WriteLine();
    }

    private static void ShowParseConversions()
    {
        Console.WriteLine("  6.2 Parse Conversions (string -> int/enum/Guid/DateTime)");
        Console.WriteLine("  ---------------------------------------------------------");

        var dto = new StringProductDto
        {
            Id = "12345",
            Quantity = "100",
            Status = "Pending"
        };

        // Auto-parses all fields from string
        var entity = NumericConversionsDto.FromEntity(dto);

        Console.WriteLine($"    DTO (strings):    Id=\"{dto.Id}\", Qty=\"{dto.Quantity}\", Status=\"{dto.Status}\"");
        Console.WriteLine($"    Entity (parsed):  Id={entity.Id}, Qty={entity.Quantity}, Status={entity.Status}");
        Console.WriteLine();
    }

    private static void ShowDateTimeConversions()
    {
        Console.WriteLine("  6.3 DateTime Conversions (DateTime <-> DateOnly/TimeOnly)");
        Console.WriteLine("  ----------------------------------------------------------");

        var event1 = new EventWithDateTime
        {
            Id = 1,
            Name = "Conference",
            OccurredAt = new DateTime(2025, 6, 15, 14, 30, 0)
        };

        // DateTime -> DateOnly/TimeOnly
        var dto = DateConversionsDto.FromEntity(event1);

        Console.WriteLine($"    Original DateTime: {event1.OccurredAt}");
        Console.WriteLine($"    DateOnly: {dto.OccurredDate}");
        Console.WriteLine($"    TimeOnly: {dto.OccurredTime}");
        Console.WriteLine();
    }

    private static void ShowDictionaryDtoMapping()
    {
        Console.WriteLine("  6.4 Dictionary with DTO Values");
        Console.WriteLine("  -------------------------------");

        var store = new Store
        {
            Id = 1,
            Name = "Main Store",
            Categories =
            {
                ["electronics"] = new Category { Id = 1, Name = "Electronics" },
                ["clothing"] = new Category { Id = 2, Name = "Clothing" },
                ["food"] = new Category { Id = 3, Name = "Food" }
            }
        };

        var storeDto = StoreDto.FromEntity(store);

        Console.WriteLine($"    Store: {storeDto.Name}");
        Console.WriteLine($"    Categories: {storeDto.Categories.Count}");
        foreach (var (key, category) in storeDto.Categories)
            Console.WriteLine($"      [{key}] -> {category.Name}");
        Console.WriteLine();
    }

    private static void ShowInheritanceMapping()
    {
        Console.WriteLine("  6.5 Inheritance Mapping (Base + Derived properties)");
        Console.WriteLine("  ----------------------------------------------------");

        var employee = new Employee
        {
            Id = 1,
            Name = "John Doe", // From Person
            Email = "john@example.com", // From Person
            Department = "Engineering", // Employee-specific
            Title = "Senior Engineer" // Employee-specific
        };

        var dto = EmployeeDto.FromEntity(employee);

        Console.WriteLine($"    Employee: {dto.Name} <{dto.Email}>");
        Console.WriteLine($"    Department: {dto.Department}, Title: {dto.Title}");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// P1: ToString/Parse - Entities and DTOs
// ═══════════════════════════════════════════════════════════════════════════════

public enum ProductStatus
{
    Pending,
    Active,
    Discontinued
}

public class ProductWithNumericFields
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public ProductStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
///     DTO with all string fields - tests X -> string conversions.
/// </summary>
[MapFrom<ProductWithNumericFields>]
public partial record StringConversionsDto
{
    public string Id { get; init; } = ""; // Guid -> string
    public string Quantity { get; init; } = ""; // int -> string
    public string Price { get; init; } = ""; // decimal -> string
    public string IsAvailable { get; init; } = ""; // bool -> string
    public string Status { get; init; } = ""; // enum -> string
    public string CreatedAt { get; init; } = ""; // DateTime -> string
}

public class StringProductDto
{
    public string Id { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>
///     DTO with numeric/enum fields - tests string -> X conversions.
/// </summary>
[MapFrom<StringProductDto>]
public partial record NumericConversionsDto
{
    public int Id { get; init; } // string -> int
    public int Quantity { get; init; } // string -> int
    public ProductStatus Status { get; init; } // string -> enum
}

// ═══════════════════════════════════════════════════════════════════════════════
// P2: DateTime Conversions
// ═══════════════════════════════════════════════════════════════════════════════

public class EventWithDateTime
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}

/// <summary>
///     DTO that extracts DateOnly and TimeOnly from DateTime.
/// </summary>
[MapFrom<EventWithDateTime>]
public partial record DateConversionsDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";

    [MapProperty("OccurredAt")] public DateOnly OccurredDate { get; init; } // DateTime -> DateOnly

    [MapProperty("OccurredAt")] public TimeOnly OccurredTime { get; init; } // DateTime -> TimeOnly
}

// ═══════════════════════════════════════════════════════════════════════════════
// P3: Dictionary with DTO Values
// ═══════════════════════════════════════════════════════════════════════════════

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[MapFrom<Category>]
public partial record CategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Dictionary<string, Category> Categories { get; set; } = new();
}

[MapFrom<Store>]
public partial record StoreDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public Dictionary<string, CategoryDto> Categories { get; init; } = new();
}

// ═══════════════════════════════════════════════════════════════════════════════
// P3: Inheritance Mapping
// ═══════════════════════════════════════════════════════════════════════════════

public abstract class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Employee : Person
{
    public string Department { get; set; } = "";
    public string Title { get; set; } = "";
}

[MapFrom<Employee>]
public partial record EmployeeDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Department { get; init; } = "";
    public string Title { get; init; } = "";
}