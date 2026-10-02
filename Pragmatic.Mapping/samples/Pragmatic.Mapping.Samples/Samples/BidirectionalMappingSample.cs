using Pragmatic.Mapping.Samples.Dtos;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates bidirectional mapping (DTO -> Entity and Entity -> DTO).
/// </summary>
public static class BidirectionalMappingSample
{
    public static void Run()
    {
        Console.WriteLine("--- Bidirectional Mapping Sample ---");

        // Create an existing entity (as if loaded from database)
        var existingProduct = new Product
        {
            Id = 1,
            Name = "Widget Pro",
            Description = "Professional-grade widget",
            Price = 99.99m,
            StockQuantity = 100,
            IsActive = true,
            CreatedAt = new DateTime(2024, 1, 1),
            UpdatedAt = null
        };

        // Map Entity -> DTO (for API response or display)
        var productDto = ProductDto.FromEntity(existingProduct);
        Console.WriteLine("Entity -> DTO:");
        Console.WriteLine($"  Id: {productDto.Id}");
        Console.WriteLine($"  Name: {productDto.Name}");
        Console.WriteLine($"  Price: {productDto.Price:C}");
        Console.WriteLine($"  Stock: {productDto.StockQuantity}");
        Console.WriteLine($"  Active: {productDto.IsActive}");

        // Modify DTO and map back to Entity (for update)
        var updatedDto = productDto with { Name = "Widget Pro Max", Price = 129.99m };
        var updatedEntity = updatedDto.ToEntity();

        Console.WriteLine();
        Console.WriteLine("DTO -> Entity (Update):");
        Console.WriteLine($"  Id: {updatedEntity.Id}");
        Console.WriteLine($"  Name: {updatedEntity.Name}");
        Console.WriteLine($"  Price: {updatedEntity.Price:C}");

        // Create request -> Entity (for new records)
        var createRequest = new CreateProductRequest
        {
            Name = "New Gadget",
            Description = "The latest gadget technology",
            Price = 199.99m,
            StockQuantity = 50
        };

        var newEntity = createRequest.ToEntity();
        Console.WriteLine();
        Console.WriteLine("CreateRequest -> Entity (New):");
        Console.WriteLine($"  Id: {newEntity.Id} (default, will be auto-generated)");
        Console.WriteLine($"  Name: {newEntity.Name}");
        Console.WriteLine($"  Price: {newEntity.Price:C}");
        Console.WriteLine($"  Stock: {newEntity.StockQuantity}");
        Console.WriteLine($"  IsActive: {newEntity.IsActive} (default value)");

        // Update request -> Entity (partial update)
        var updateRequest = new UpdateProductRequest
        {
            Id = 1,
            Name = "Updated Widget",
            Description = "Updated description",
            Price = 149.99m,
            RequestedBy = 42 // This is ignored in mapping
        };

        var patchedEntity = updateRequest.ToEntity();
        Console.WriteLine();
        Console.WriteLine("UpdateRequest -> Entity (Patch):");
        Console.WriteLine($"  Id: {patchedEntity.Id}");
        Console.WriteLine($"  Name: {patchedEntity.Name}");
        Console.WriteLine($"  Price: {patchedEntity.Price:C}");
        Console.WriteLine($"  StockQuantity: {patchedEntity.StockQuantity} (not mapped from request)");

        Console.WriteLine();
    }
}