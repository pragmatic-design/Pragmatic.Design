using System.Runtime.InteropServices;
using Pragmatic.Mapping.Samples.Dtos;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates struct/record struct mapping for zero-allocation scenarios.
/// </summary>
public static class StructMappingSample
{
    public static void Run()
    {
        Console.WriteLine("--- Struct Mapping Sample ---");

        // Coordinate mapping with record struct (value type, no heap allocation)
        var coordinate = new Coordinate
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Altitude = 10.5,
            Label = "New York City",
            Timestamp = DateTime.Now
        };

        // Map to record struct - zero heap allocation
        var coordDto = CoordinateDto.FromEntity(coordinate);
        Console.WriteLine("Record Struct (CoordinateDto):");
        Console.WriteLine($"  Latitude: {coordDto.Latitude}");
        Console.WriteLine($"  Longitude: {coordDto.Longitude}");
        Console.WriteLine($"  Altitude: {coordDto.Altitude}");
        Console.WriteLine($"  Label: {coordDto.Label}");
        Console.WriteLine($"  Size: {Marshal.SizeOf<CoordinateDto>()} bytes");

        // Map to regular struct
        var pointDto = PointDto.FromEntity(coordinate);
        Console.WriteLine();
        Console.WriteLine("Regular Struct (PointDto):");
        Console.WriteLine($"  ({pointDto.Latitude}, {pointDto.Longitude})");

        // Collection of struct DTOs
        var coordinates = new List<Coordinate>
        {
            coordinate,
            new() { Latitude = 34.0522, Longitude = -118.2437, Label = "Los Angeles" },
            new() { Latitude = 41.8781, Longitude = -87.6298, Label = "Chicago" }
        };

        // Map to array of structs - efficient for bulk operations
        var coordArray = coordinates.ToCoordinateDto().ToArray();
        Console.WriteLine();
        Console.WriteLine($"Array of {coordArray.Length} struct DTOs:");
        foreach (var c in coordArray)
            Console.WriteLine($"  {c.Label}: ({c.Latitude:F4}, {c.Longitude:F4})");

        // Invoice with embedded struct
        var invoice = new Invoice
        {
            Id = 1001,
            InvoiceNumber = "INV-2024-001",
            Amount = 1500.00m,
            Currency = "USD",
            DueDate = new DateTime(2024, 8, 15)
        };

        var invoiceDto = InvoiceDto.FromEntity(invoice);
        Console.WriteLine();
        Console.WriteLine("Invoice DTO:");
        Console.WriteLine($"  {invoiceDto.InvoiceNumber}");
        Console.WriteLine($"  Amount: {invoiceDto.Amount} {invoiceDto.Currency}");
        Console.WriteLine($"  Due: {invoiceDto.DueDate}");

        Console.WriteLine();
    }
}