using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates all source → target type combinations.
/// </summary>
public static class TypeCombinationsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Type Combinations (class/record/struct)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowClassToClass();
        ShowClassToRecord();
        ShowRecordToRecord();
        ShowRecordToClass();

        Console.WriteLine();
    }

    private static void ShowClassToClass()
    {
        Console.WriteLine("  8.1 Class → Class Mapping");
        Console.WriteLine("  --------------------------");

        var entity = new ProductEntity
        {
            Id = 1,
            Name = "Widget",
            Price = 29.99m,
            InternalCode = "WIDGET-001" // Will be ignored
        };

        var dto = ProductClass.FromEntity(entity);

        Console.WriteLine($"    Entity (class):  Id={entity.Id}, Name={entity.Name}, Price={entity.Price}");
        Console.WriteLine($"    DTO (class):     Id={dto.Id}, Name={dto.Name}, Price={dto.Price}");
        Console.WriteLine($"    [MapIgnore]:     InternalCode not mapped (would be \"{entity.InternalCode}\")");
        Console.WriteLine();
    }

    private static void ShowClassToRecord()
    {
        Console.WriteLine("  8.2 Class → Record Mapping (most common)");
        Console.WriteLine("  -----------------------------------------");

        var entity = new ProductEntity
        {
            Id = 2,
            Name = "Gadget",
            Price = 49.99m,
            InternalCode = "GADGET-001"
        };

        var dto = ProductRecord.FromEntity(entity);

        Console.WriteLine($"    Entity (class):  Id={entity.Id}, Name={entity.Name}");
        Console.WriteLine($"    DTO (record):    Id={dto.Id}, Name={dto.Name}");
        Console.WriteLine();
    }

    private static void ShowRecordToRecord()
    {
        Console.WriteLine("  8.3 Record → Record Mapping");
        Console.WriteLine("  ----------------------------");

        var source = new CustomerRecord(100, "Alice", "alice@example.com");
        var dto = CustomerRecordDto.FromEntity(source);

        Console.WriteLine($"    Source (record): Id={source.Id}, Name={source.Name}, Email={source.Email}");
        Console.WriteLine($"    DTO (record):    Id={dto.Id}, Name={dto.Name}, Email={dto.Email}");
        Console.WriteLine();
    }

    private static void ShowRecordToClass()
    {
        Console.WriteLine("  8.4 Record → Class Mapping");
        Console.WriteLine("  ---------------------------");

        var source = new CustomerRecord(200, "Bob", "bob@example.com");
        var dto = CustomerClassDto.FromEntity(source);

        Console.WriteLine($"    Source (record): Id={source.Id}, Name={source.Name}");
        Console.WriteLine($"    DTO (class):     Id={dto.Id}, Name={dto.Name}");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 8.1: Class → Class
// ═══════════════════════════════════════════════════════════════════════════════

public class ProductEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string InternalCode { get; set; } = ""; // Internal, don't expose
}

/// <summary>
///     Class DTO - shows class → class mapping with MapIgnore.
/// </summary>
[MapFrom<ProductEntity>]
public partial class ProductClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }

    [MapIgnore] // Don't map internal code to DTO
    public string InternalCode { get; set; } = "HIDDEN";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 8.2: Class → Record (most common pattern)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
///     Record DTO - immutable, generated value equality.
/// </summary>
[MapFrom<ProductEntity>]
public partial record ProductRecord
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 8.3: Record → Record
// ═══════════════════════════════════════════════════════════════════════════════

public record CustomerRecord(int Id, string Name, string Email);

[MapFrom<CustomerRecord>]
public partial record CustomerRecordDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 8.4: Record → Class
// ═══════════════════════════════════════════════════════════════════════════════

[MapFrom<CustomerRecord>]
public partial class CustomerClassDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}