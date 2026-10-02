using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Converters;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates MapIgnore, MapConverter, and MapProperty with Format/Default.
/// </summary>
public static class AdvancedAttributesSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. Advanced Attributes (MapIgnore, MapConverter, MapProperty)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowMapIgnore();
        ShowMapConverter();
        ShowMapPropertyExplicitPath();
        ShowMapPropertyFormat();
        ShowMapPropertyWithConverter();

        Console.WriteLine();
    }

    private static void ShowMapIgnore()
    {
        Console.WriteLine("  9.1 [MapIgnore] - Exclude properties from mapping");
        Console.WriteLine("  -------------------------------------------------");

        var entity = new SecretEntity
        {
            Id = 1,
            PublicName = "Public Item",
            Password = "secret123",
            ApiKey = "sk-xxx-yyy"
        };

        var dto = SecretDto.FromEntity(entity);

        Console.WriteLine($"    Entity:  PublicName=\"{entity.PublicName}\", Password=\"{entity.Password}\"");
        Console.WriteLine($"    DTO:     PublicName=\"{dto.PublicName}\", Password=\"{dto.Password}\" (ignored!)");
        Console.WriteLine($"             ApiKey=\"{dto.ApiKey}\" (has default value)");
        Console.WriteLine();
    }

    private static void ShowMapConverter()
    {
        Console.WriteLine("  9.2 [MapConverter] - Custom value conversion");
        Console.WriteLine("  --------------------------------------------");

        var entity = new DocumentEntity
        {
            Id = 1,
            Title = "Annual Report",
            Code = "doc-2025-001" // Will be uppercased
        };

        var dto = DocumentDto.FromEntity(entity);

        Console.WriteLine($"    Entity:  Code=\"{entity.Code}\" (lowercase)");
        Console.WriteLine($"    DTO:     Code=\"{dto.Code}\" (converted to uppercase)");
        Console.WriteLine();
    }

    private static void ShowMapPropertyExplicitPath()
    {
        Console.WriteLine("  9.3 [MapProperty(\"path\")] - Explicit property path");
        Console.WriteLine("  ---------------------------------------------------");

        var entity = new PersonEntity
        {
            Id = 1,
            Surname = "Smith",
            GivenName = "John",
            HomeAddress = new AddressInfo { City = "New York", ZipCode = "10001" }
        };

        var dto = PersonDto.FromEntity(entity);

        Console.WriteLine($"    Entity:  Surname=\"{entity.Surname}\", GivenName=\"{entity.GivenName}\"");
        Console.WriteLine($"    DTO:     LastName=\"{dto.LastName}\" (from Surname)");
        Console.WriteLine($"             FirstName=\"{dto.FirstName}\" (from GivenName)");
        Console.WriteLine($"             PostalCode=\"{dto.PostalCode}\" (from HomeAddress.ZipCode)");
        Console.WriteLine();
    }

    private static void ShowMapPropertyFormat()
    {
        Console.WriteLine("  9.4 [MapProperty(Format = \"...\")] - Formatted strings");
        Console.WriteLine("  ------------------------------------------------------");

        var entity = new EventEntity
        {
            Id = 1,
            Name = "Conference",
            StartDate = new DateTime(2025, 6, 15, 9, 0, 0),
            TicketPrice = 299.99m
        };

        var dto = EventDto.FromEntity(entity);

        Console.WriteLine($"    Entity:  StartDate={entity.StartDate}, Price={entity.TicketPrice}");
        Console.WriteLine($"    DTO:     StartDateText=\"{dto.StartDateText}\" (yyyy-MM-dd format)");
        Console.WriteLine($"             PriceText=\"{dto.PriceText}\" (F2 fixed decimal format)");
        Console.WriteLine();
    }

    private static void ShowMapPropertyWithConverter()
    {
        Console.WriteLine("  9.5 [MapProperty] + [MapConverter] - Combined usage");
        Console.WriteLine("  ---------------------------------------------------");

        var entity = new FileEntity
        {
            Id = 1,
            Name = "annual-report.pdf",
            SizeBytes = 2_621_440 // 2.5 MB
        };

        var dto = FileDto.FromEntity(entity);

        Console.WriteLine($"    Entity:  SizeBytes={entity.SizeBytes} (raw bytes, property named 'SizeBytes')");
        Console.WriteLine($"    DTO:     SizeDisplay=\"{dto.SizeDisplay}\" (property named 'SizeDisplay')");
        Console.WriteLine("             [MapProperty(\"SizeBytes\")] + [MapConverter<BytesToHumanReadableConverter>]");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9.1: [MapIgnore] - Exclude sensitive properties
// ═══════════════════════════════════════════════════════════════════════════════

public class SecretEntity
{
    public int Id { get; set; }
    public string PublicName { get; set; } = "";
    public string Password { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

[MapFrom<SecretEntity>]
public partial record SecretDto
{
    public int Id { get; init; }
    public string PublicName { get; init; } = "";

    [MapIgnore] // Never map password to DTO
    public string Password { get; init; } = "***";

    [MapIgnore] // Never map API key to DTO
    public string ApiKey { get; init; } = "[REDACTED]";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9.2: [MapConverter] - Custom value conversion
// ═══════════════════════════════════════════════════════════════════════════════

public class DocumentEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Code { get; set; } = ""; // Will be uppercased
}

/// <summary>
///     Converts string to uppercase (simple example).
/// </summary>
public class UpperCaseConverter : IValueConverter<string, string>
{
    public string Convert(string source)
    {
        return source.ToUpperInvariant();
    }

    public string ConvertBack(string target)
    {
        return target.ToLowerInvariant();
    }
}

[MapFrom<DocumentEntity>]
public partial record DocumentDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";

    // MapConverter: property names must match (Code → Code)
    // The converter transforms the value during mapping
    [MapConverter<UpperCaseConverter>] public string Code { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9.3: [MapProperty("path")] - Explicit property path
// ═══════════════════════════════════════════════════════════════════════════════

public class AddressInfo
{
    public string City { get; set; } = "";
    public string ZipCode { get; set; } = "";
}

public class PersonEntity
{
    public int Id { get; set; }
    public string Surname { get; set; } = ""; // Different naming
    public string GivenName { get; set; } = ""; // Different naming
    public AddressInfo? HomeAddress { get; set; } // Nested object
}

[MapFrom<PersonEntity>]
public partial record PersonDto
{
    public int Id { get; init; }

    [MapProperty("Surname")] // Explicit: Surname → LastName
    public string LastName { get; init; } = "";

    [MapProperty("GivenName")] // Explicit: GivenName → FirstName
    public string FirstName { get; init; } = "";

    [MapProperty("HomeAddress.ZipCode")] // Nested path
    public string? PostalCode { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9.4: [MapProperty(Format = "...")] - Formatted strings
// ═══════════════════════════════════════════════════════════════════════════════

public class EventEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public decimal TicketPrice { get; set; }
}

[MapFrom<EventEntity>]
public partial record EventDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";

    [MapProperty(nameof(EventEntity.StartDate), Format = "yyyy-MM-dd")]
    public string StartDateText { get; init; } = "";

    [MapProperty(nameof(EventEntity.TicketPrice), Format = "F2")] // Fixed decimal format
    public string PriceText { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 9.5: [MapProperty] + [MapConverter] - Combined usage
// ═══════════════════════════════════════════════════════════════════════════════

public class FileEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; } // Different name: SizeBytes
}

/// <summary>
///     Converts bytes to human-readable format (KB, MB, GB).
/// </summary>
public class BytesToHumanReadableConverter : IValueConverter<long, string>
{
    public string Convert(long source)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        double size = source;

        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:F2} {suffixes[index]}";
    }

    public long ConvertBack(string target)
    {
        var parts = target.Split(' ');
        if (parts.Length != 2)
            return 0;

        var value = double.Parse(parts[0]);
        var suffix = parts[1].ToUpperInvariant();

        return suffix switch
        {
            "B" => (long)value,
            "KB" => (long)(value * 1024),
            "MB" => (long)(value * 1024 * 1024),
            "GB" => (long)(value * 1024 * 1024 * 1024),
            "TB" => (long)(value * 1024 * 1024 * 1024 * 1024),
            _ => 0
        };
    }
}

[MapFrom<FileEntity>]
public partial record FileDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";

    // Combines MapProperty (different source name) with MapConverter (custom conversion)
    [MapProperty("SizeBytes")]
    [MapConverter<BytesToHumanReadableConverter>]
    public string SizeDisplay { get; init; } = "";
}