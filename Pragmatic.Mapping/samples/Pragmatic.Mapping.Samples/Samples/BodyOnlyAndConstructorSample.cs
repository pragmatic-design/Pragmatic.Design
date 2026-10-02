using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates [GenerateBodyOnlyVariant] for scalar-only mapping (no collections/nested)
///     and [MapConstructor] for explicit constructor selection.
/// </summary>
public static class BodyOnlyAndConstructorSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("12. BodyOnly Variant & MapConstructor Samples");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowBodyOnlyVariant();
        ShowMapConstructor();
        ShowBestMatchConstructor();

        Console.WriteLine();
    }

    private static void ShowBodyOnlyVariant()
    {
        Console.WriteLine("  12.1 [GenerateBodyOnlyVariant] — Scalar-only mapping");
        Console.WriteLine("  ----------------------------------------------------");

        var invoice = new InvoiceEntity
        {
            Id = 1,
            InvoiceNumber = "INV-2025-001",
            Total = 1250.00m,
            Currency = "EUR",
            BillingAddress = new BillingAddress
            {
                Street = "Via Roma 42",
                City = "Milano",
                PostalCode = "20100"
            },
            Lines =
            [
                new InvoiceLine { Description = "Consulting", Amount = 1000m },
                new InvoiceLine { Description = "Travel", Amount = 250m }
            ]
        };

        // FromEntity maps everything (scalars + nested + collections)
        var fullDto = InvoiceOverviewDto.FromEntity(invoice);
        Console.WriteLine($"    FromEntity (full):      #{fullDto.InvoiceNumber} - {fullDto.Total:C} {fullDto.Currency}");
        Console.WriteLine($"                            Address: {fullDto.BillingAddress?.City}");
        Console.WriteLine($"                            Lines: {fullDto.Lines.Count}");

        // FromEntityBodyOnly maps ONLY scalar properties (no nested DTO, no collections)
        var bodyOnly = InvoiceOverviewDto.FromEntityBodyOnly(invoice);
        Console.WriteLine();
        Console.WriteLine($"    FromEntityBodyOnly:     #{bodyOnly.InvoiceNumber} - {bodyOnly.Total:C} {bodyOnly.Currency}");
        Console.WriteLine($"                            Address: {(bodyOnly.BillingAddress == null ? "(null — skipped)" : bodyOnly.BillingAddress.City)}");
        Console.WriteLine($"                            Lines: {bodyOnly.Lines.Count} (empty — skipped)");
        Console.WriteLine();

        Console.WriteLine("    Use case: mutation init needs only scalar fields from entity,");
        Console.WriteLine("    navigation/collection handling is done separately.");
        Console.WriteLine();
    }

    private static void ShowMapConstructor()
    {
        Console.WriteLine("  12.2 [MapConstructor] — Explicit constructor selection");
        Console.WriteLine("  -------------------------------------------------------");

        // DTO with values to map to an entity that has multiple constructors
        var createDto = new CreateImmutableProductDto
        {
            Sku = "WIDGET-001",
            Name = "Premium Widget",
            Price = 49.99m
        };

        // ToEntity uses the constructor marked with [MapConstructor]
        var product = createDto.ToEntity();

        Console.WriteLine($"    DTO:    Sku=\"{createDto.Sku}\", Name=\"{createDto.Name}\", Price={createDto.Price:C}");
        Console.WriteLine($"    Entity: Sku=\"{product.Sku}\", Name=\"{product.Name}\", Price={product.Price:C}");
        Console.WriteLine($"            CreatedVia=\"{product.CreatedVia}\"");
        Console.WriteLine();

        Console.WriteLine("    The entity has 3 constructors — [MapConstructor] ensures the");
        Console.WriteLine("    generator always picks the right one (sku, name, price).");
        Console.WriteLine();
    }

    private static void ShowBestMatchConstructor()
    {
        Console.WriteLine("  12.3 Best-Match Constructor — SG picks the best constructor automatically");
        Console.WriteLine("  --------------------------------------------------------------------------");

        // Entity has 2 constructors. Without [MapConstructor], the SG picks
        // the one with the most matched parameters from the DTO.
        var dto = new CreateConfigEntryDto
        {
            Key = "feature.enabled",
            Value = "true",
            Description = "Toggle new feature"
        };

        var entity = dto.ToEntity();

        Console.WriteLine($"    DTO:    Key=\"{dto.Key}\", Value=\"{dto.Value}\", Description=\"{dto.Description}\"");
        Console.WriteLine($"    Entity: Key=\"{entity.Key}\", Value=\"{entity.Value}\"");
        Console.WriteLine($"            Description=\"{entity.Description}\"");
        Console.WriteLine($"            Source=\"{entity.Source}\" (default from constructor)");
        Console.WriteLine();
        Console.WriteLine("    The SG picked constructor(key, value, description) — best match.");
        Console.WriteLine("    The default constructor would leave all empty.");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 12.1: BodyOnly Variant
// ═══════════════════════════════════════════════════════════════════════════════

public class InvoiceEntity
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public BillingAddress? BillingAddress { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
}

public class BillingAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
}

public class InvoiceLine
{
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}

[MapFrom<BillingAddress>]
public partial record BillingAddressDto
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
    public string PostalCode { get; init; } = "";
}

[MapFrom<InvoiceLine>]
public partial record InvoiceLineDto
{
    public string Description { get; init; } = "";
    public decimal Amount { get; init; }
}

/// <summary>
///     DTO with [GenerateBodyOnlyVariant] — generates both FromEntity() and FromEntityBodyOnly().
///     BodyOnly skips BillingAddress (nested DTO) and Lines (collection of DTOs).
/// </summary>
[MapFrom<InvoiceEntity>]
[GenerateBodyOnlyVariant]
public partial record InvoiceOverviewDto
{
    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public decimal Total { get; init; }
    public string Currency { get; init; } = "";
    public BillingAddressDto? BillingAddress { get; init; }      // Skipped in BodyOnly
    public List<InvoiceLineDto> Lines { get; init; } = [];       // Skipped in BodyOnly
}

// ═══════════════════════════════════════════════════════════════════════════════
// 12.2: MapConstructor
// ═══════════════════════════════════════════════════════════════════════════════

public class ImmutableProduct
{
    public string Sku { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string CreatedVia { get; set; }

    // Default constructor (would be chosen without [MapConstructor])
    public ImmutableProduct()
    {
        Sku = "";
        Name = "";
        Price = 0;
        CreatedVia = "default";
    }

    // Constructor with only name (not the best fit for mapping)
    public ImmutableProduct(string name)
    {
        Sku = "";
        Name = name;
        Price = 0;
        CreatedVia = "name-only";
    }

    // This is the constructor we want for mapping — marked with [MapConstructor]
    [MapConstructor]
    public ImmutableProduct(string sku, string name, decimal price)
    {
        Sku = sku;
        Name = name;
        Price = price;
        CreatedVia = "full-mapping";
    }
}

[MapTo<ImmutableProduct>]
public partial record CreateImmutableProductDto
{
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 12.3: Best-Match Constructor (no [MapConstructor] — SG auto-picks)
// ═══════════════════════════════════════════════════════════════════════════════

public class ConfigEntry
{
    public string Key { get; set; }
    public string Value { get; set; }
    public string Description { get; set; }
    public string Source { get; set; }

    // Default constructor
    public ConfigEntry()
    {
        Key = "";
        Value = "";
        Description = "";
        Source = "default";
    }

    // Best match: 3 params matching DTO properties
    public ConfigEntry(string key, string value, string description)
    {
        Key = key;
        Value = value;
        Description = description;
        Source = "parameterized";
    }
}

[MapTo<ConfigEntry>]
public partial record CreateConfigEntryDto
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public string Description { get; init; } = "";
}
