using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Mutation;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates ApplyTo() for partial updates on existing entities,
///     nested MapTo with recursive ToEntity/ApplyTo, and TargetPath for nested property assignment.
/// </summary>
public static class ApplyToSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("11. ApplyTo, Nested MapTo & TargetPath Samples");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowBasicApplyTo();
        ShowNestedMapToAndApplyTo();
        ShowTargetPathMapping();

        Console.WriteLine();
    }

    private static void ShowBasicApplyTo()
    {
        Console.WriteLine("  11.1 ApplyTo — Partial Update on Existing Entity");
        Console.WriteLine("  -------------------------------------------------");

        // Simulate loading an entity from the database
        var existingArticle = new Article
        {
            Id = 42,
            Title = "Original Title",
            Body = "Original body content...",
            Author = "Jane Doe",
            PublishedAt = new DateTime(2025, 1, 15)
        };

        Console.WriteLine($"    Before: Title=\"{existingArticle.Title}\", Author=\"{existingArticle.Author}\"");

        // Build an update DTO (e.g., from an API PATCH request)
        var updateDto = new UpdateArticleDto
        {
            Title = "Updated Title",
            Body = "New improved content!"
            // Author is NOT set — it stays as-is in the entity
        };

        // ApplyTo updates only the mapped properties, leaves others intact
        updateDto.ApplyTo(existingArticle);

        Console.WriteLine($"    After:  Title=\"{existingArticle.Title}\", Author=\"{existingArticle.Author}\"");
        Console.WriteLine($"            Body=\"{existingArticle.Body}\"");
        Console.WriteLine($"            PublishedAt={existingArticle.PublishedAt} (untouched)");
        Console.WriteLine();
    }

    private static void ShowNestedMapToAndApplyTo()
    {
        Console.WriteLine("  11.2 Nested MapTo — ToEntity with nested DTOs");
        Console.WriteLine("  -----------------------------------------------");

        // Create a purchase order DTO with nested line DTOs
        var orderDto = new CreatePurchaseOrderDto
        {
            Vendor = "Acme Supplies",
            Lines =
            [
                new CreatePurchaseLineDto { ProductName = "Widget A", Quantity = 10, UnitPrice = 9.99m },
                new CreatePurchaseLineDto { ProductName = "Widget B", Quantity = 5, UnitPrice = 24.50m }
            ]
        };

        // ToEntity recursively converts all nested DTOs
        var entity = orderDto.ToEntity();

        Console.WriteLine($"    Order Vendor: {entity.Vendor}");
        Console.WriteLine($"    Lines ({entity.Lines.Count}):");
        foreach (var line in entity.Lines)
            Console.WriteLine($"      - {line.ProductName} x{line.Quantity} @ {line.UnitPrice:C}");

        // ApplyTo on an existing entity with nested data
        var existingOrder = new PurchaseOrder
        {
            Id = 99,
            Vendor = "Old Vendor",
            Lines = [new PurchaseLine { ProductName = "Old item", Quantity = 1, UnitPrice = 0m }]
        };

        Console.WriteLine();
        Console.WriteLine($"    Before ApplyTo: Vendor=\"{existingOrder.Vendor}\", Lines={existingOrder.Lines.Count}");

        orderDto.ApplyTo(existingOrder);

        Console.WriteLine($"    After ApplyTo:  Vendor=\"{existingOrder.Vendor}\", Lines={existingOrder.Lines.Count}");
        Console.WriteLine();
    }

    private static void ShowTargetPathMapping()
    {
        Console.WriteLine("  11.3 TargetPath — Map DTO property to nested entity path");
        Console.WriteLine("  ---------------------------------------------------------");

        var dto = new UpdateShipmentDto
        {
            TrackingNumber = "TRACK-123",
            RecipientName = "Alice Smith",
            RecipientCity = "New York"
        };

        // ToEntity creates the entity and assigns nested paths
        var entity = dto.ToEntity();

        Console.WriteLine($"    DTO: TrackingNumber=\"{dto.TrackingNumber}\"");
        Console.WriteLine($"         RecipientName=\"{dto.RecipientName}\"");
        Console.WriteLine($"         RecipientCity=\"{dto.RecipientCity}\"");
        Console.WriteLine();
        Console.WriteLine($"    Entity: TrackingNumber=\"{entity.TrackingNumber}\"");
        Console.WriteLine($"            Recipient.Name=\"{entity.Recipient.Name}\"");
        Console.WriteLine($"            Recipient.City=\"{entity.Recipient.City}\"");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 11.1: Basic ApplyTo
// ═══════════════════════════════════════════════════════════════════════════════

public class Article
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime PublishedAt { get; set; }
}

/// <summary>
///     Update DTO — only Title and Body are mapped. Author and PublishedAt are excluded.
///     ApplyTo() will update only mapped properties on an existing entity.
/// </summary>
[MapTo<Article>]
public partial record UpdateArticleDto
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    // Author and PublishedAt intentionally omitted — ApplyTo won't touch them
}

// ═══════════════════════════════════════════════════════════════════════════════
// 11.2: Nested MapTo (ToEntity + ApplyTo with nested DTOs)
// ═══════════════════════════════════════════════════════════════════════════════

public class PurchaseOrder
{
    public int Id { get; set; }
    public string Vendor { get; set; } = "";
    public List<PurchaseLine> Lines { get; set; } = [];
}

public class PurchaseLine
{
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

[MapTo<PurchaseOrder>]
public partial record CreatePurchaseOrderDto
{
    public string Vendor { get; init; } = "";

    // A purchase line has no identity of its own here, so there is nothing to match an incoming line
    // against an existing one by. Replace says what this DTO means: the lines it carries are the lines.
    [CollectionStrategy(CollectionStrategy.Replace)]
    public List<CreatePurchaseLineDto> Lines { get; init; } = [];
}

[MapTo<PurchaseLine>]
public partial record CreatePurchaseLineDto
{
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 11.3: TargetPath — [MapProperty(Target = "nested.path")]
// ═══════════════════════════════════════════════════════════════════════════════

public class Shipment
{
    public string TrackingNumber { get; set; } = "";
    public ShipmentRecipient Recipient { get; set; } = new();
}

public class ShipmentRecipient
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
}

/// <summary>
///     Flat DTO that maps to nested entity properties via TargetPath.
///     RecipientName -> Recipient.Name, RecipientCity -> Recipient.City.
/// </summary>
[MapTo<Shipment>]
public partial record UpdateShipmentDto
{
    public string TrackingNumber { get; init; } = "";

    [MapProperty(Target = "Recipient.Name")]
    public string RecipientName { get; init; } = "";

    [MapProperty(Target = "Recipient.City")]
    public string RecipientCity { get; init; } = "";
}
