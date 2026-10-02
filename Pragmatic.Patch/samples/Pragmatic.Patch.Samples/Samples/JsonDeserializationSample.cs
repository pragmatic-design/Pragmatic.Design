using System.Text.Json;

namespace Pragmatic.Patch.Samples.Samples;

/// <summary>
///     JSON deserialization demo: shows how HTTP PATCH JSON bodies
///     are deserialized into Optional&lt;T&gt; properties with tri-state.
/// </summary>
public static class JsonDeserializationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. JSON Deserialization — Tri-State from HTTP Body");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowPartialJsonBody();
        ShowNullJsonBody();
        ShowEmptyJsonBody();
        ShowFullJsonBody();

        Console.WriteLine();
    }

    private static void ShowPartialJsonBody()
    {
        Console.WriteLine("  4.1 Partial body — only some fields sent");
        Console.WriteLine("  --------------------------------------------");

        var json = """{"Name": "Updated Widget", "Price": 19.99}""";
        var patch = JsonSerializer.Deserialize<PatchProduct>(json)!;

        Console.WriteLine($"    JSON: {json}");
        Console.WriteLine($"    Name:        HasValue={patch.Name.HasValue}, Value=\"{patch.Name.Value}\"");
        Console.WriteLine($"    Price:       HasValue={patch.Price.HasValue}, Value={patch.Price.Value}");
        Console.WriteLine($"    Description: HasValue={patch.Description.HasValue} (not sent → Undefined)");
        Console.WriteLine($"    Stock:       HasValue={patch.Stock.HasValue} (not sent → Undefined)");
        Console.WriteLine($"    Modified:    [{string.Join(", ", patch.ModifiedProperties)}]");
        Console.WriteLine();
    }

    private static void ShowNullJsonBody()
    {
        Console.WriteLine("  4.2 Explicit null — field sent as null");
        Console.WriteLine("  ------------------------------------------");

        var json = """{"Description": null}""";
        var patch = JsonSerializer.Deserialize<PatchProduct>(json)!;

        Console.WriteLine($"    JSON: {json}");
        Console.WriteLine($"    Description: HasValue={patch.Description.HasValue}, Value={(patch.Description.Value is null ? "(null)" : patch.Description.Value)}");
        Console.WriteLine($"    This is DIFFERENT from not sending the field.");
        Console.WriteLine($"    Null means \"clear this field\", Undefined means \"leave it alone\".");
        Console.WriteLine();
    }

    private static void ShowEmptyJsonBody()
    {
        Console.WriteLine("  4.3 Empty body — nothing changes");
        Console.WriteLine("  -----------------------------------");

        var json = "{}";
        var patch = JsonSerializer.Deserialize<PatchProduct>(json)!;

        Console.WriteLine($"    JSON: {json}");
        Console.WriteLine($"    All properties Undefined: {patch.ModifiedProperties.Count == 0}");
        Console.WriteLine($"    ApplyTo() would change nothing.");
        Console.WriteLine();
    }

    private static void ShowFullJsonBody()
    {
        Console.WriteLine("  4.4 Full body — all fields sent");
        Console.WriteLine("  -----------------------------------");

        var json = """{"Name": "New Name", "Description": "New desc", "Price": 49.99, "Stock": 100}""";
        var patch = JsonSerializer.Deserialize<PatchProduct>(json)!;

        Console.WriteLine($"    JSON: {json}");
        Console.WriteLine($"    Modified: [{string.Join(", ", patch.ModifiedProperties)}]");

        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Old", Description = "Old desc", Price = 0m
        };

        Console.WriteLine($"    Before: Name=\"{product.Name}\", Desc=\"{product.Description}\", Price={product.Price}, Stock={product.Stock}");
        patch.ApplyTo(product);
        Console.WriteLine($"    After:  Name=\"{product.Name}\", Desc=\"{product.Description}\", Price={product.Price}, Stock={product.Stock}");
        Console.WriteLine();
    }
}
