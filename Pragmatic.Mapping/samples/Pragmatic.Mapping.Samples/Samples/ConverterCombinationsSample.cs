using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Converters;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Advanced converter scenarios: multiple converters per DTO, bidirectional converters,
///     converter combined with nesting and MapProperty, mixed attribute combos on one DTO.
/// </summary>
public static class ConverterCombinationsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("15. Converter Combinations & Mixed Attribute Scenarios");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowMultipleConvertersPerDto();
        ShowConverterWithTypeChange();
        ShowMixedAttributeCombo();

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 15.1 Multiple converters on the same DTO
    // ─────────────────────────────────────────────────────────────────────────

    private static void ShowMultipleConvertersPerDto()
    {
        Console.WriteLine("  15.1 Multiple Converters — Different converters on same DTO");
        Console.WriteLine("  ------------------------------------------------------------");

        var sensor = new SensorReading
        {
            SensorId = "TEMP-042",
            TemperatureCelsius = 23.5,
            HumidityPercent = 65.2,
            PressureHpa = 1013.25,
            ReadingTimestamp = new DateTime(2025, 6, 15, 14, 30, 0),
            IsActive = true
        };

        var dto = SensorDisplayDto.FromEntity(sensor);

        Console.WriteLine($"    Entity: Temp={sensor.TemperatureCelsius}°C, Humidity={sensor.HumidityPercent}%");
        Console.WriteLine($"            Pressure={sensor.PressureHpa} hPa, Active={sensor.IsActive}");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    Temperature=\"{dto.Temperature}\" (CelsiusToDisplayConverter)");
        Console.WriteLine($"            Humidity=\"{dto.Humidity}\" (PercentToBarConverter)");
        Console.WriteLine($"            Pressure=\"{dto.Pressure}\" (PressureToDisplayConverter)");
        Console.WriteLine($"            Status=\"{dto.Status}\" (BoolToActiveStatusConverter)");
        Console.WriteLine($"            ReadingTime=\"{dto.ReadingTime}\" (Format: HH:mm:ss)");
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 15.2 Bidirectional converter (FromEntity + ToEntity with ConvertBack)
    // ─────────────────────────────────────────────────────────────────────────

    private static void ShowConverterWithTypeChange()
    {
        Console.WriteLine("  15.2 Converter with Type Change — List<string> -> CSV, int -> readable");
        Console.WriteLine("  -----------------------------------------------------------------------");

        var blogPost = new BlogPost
        {
            Id = 1,
            Title = "Getting Started with Mapping",
            Tags = ["csharp", "source-generators", "pragmatic"],
            WordCount = 1500
        };

        var dto = BlogPostDto.FromEntity(blogPost);

        Console.WriteLine($"    Entity: Tags=[{string.Join(", ", blogPost.Tags)}], WordCount={blogPost.WordCount}");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    TagsCsv=\"{dto.TagsCsv}\" (TagListToCsvConverter: List -> CSV)");
        Console.WriteLine($"            ReadTime=\"{dto.ReadTime}\" (WordCountToReadTimeConverter: int -> text)");
        Console.WriteLine();

        // ConvertBack is used by MutationHelpers at runtime, not by SG-generated MapTo
        Console.WriteLine("    Note: Converters work in [MapFrom] direction. For [MapTo],");
        Console.WriteLine("    use matching entity property types or manual mapping.");
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 15.3 Mixed attributes: converter + MapProperty + MapIgnore + nesting
    // ─────────────────────────────────────────────────────────────────────────

    private static void ShowMixedAttributeCombo()
    {
        Console.WriteLine("  15.3 Mixed Attributes — Converter + MapProperty + MapIgnore on one DTO");
        Console.WriteLine("  -----------------------------------------------------------------------");

        var ticket = new SupportTicket
        {
            TicketNumber = "TKT-2025-0042",
            Subject = "Login not working",
            Priority = TicketPriority.High,
            CreatedAt = new DateTime(2025, 3, 15, 9, 30, 0),
            Reporter = new TicketPerson
            {
                FirstName = "Marco",
                LastName = "Rossi",
                Email = "marco.rossi@company.com",
                InternalId = 42
            },
            Assignee = new TicketPerson
            {
                FirstName = "Alice",
                LastName = "Johnson",
                Email = "alice.j@support.com",
                InternalId = 7
            }
        };

        var dto = TicketSummaryDto.FromEntity(ticket);

        Console.WriteLine($"    Entity: #{ticket.TicketNumber} - \"{ticket.Subject}\"");
        Console.WriteLine($"            Priority={ticket.Priority}, Created={ticket.CreatedAt}");
        Console.WriteLine($"            Reporter: {ticket.Reporter.FirstName} {ticket.Reporter.LastName} (InternalId={ticket.Reporter.InternalId})");
        Console.WriteLine($"            Assignee: {ticket.Assignee.FirstName} {ticket.Assignee.LastName}");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    Reference=\"{dto.Reference}\" (from TicketNumber)");
        Console.WriteLine($"            Title=\"{dto.Title}\" (from Subject)");
        Console.WriteLine($"            PriorityIcon=\"{dto.PriorityIcon}\" (converter: enum -> icon)");
        Console.WriteLine($"            OpenedDate=\"{dto.OpenedDate}\" (from CreatedAt, format yyyy-MM-dd)");
        Console.WriteLine($"            ReporterName=\"{dto.ReporterName}\" (concat: Reporter.FirstName + Reporter.LastName)");
        Console.WriteLine($"            ReporterEmail=\"{dto.ReporterEmail}\" (from Reporter.Email)");
        Console.WriteLine($"            AssigneeName=\"{dto.AssigneeName}\" (concat: Assignee.FirstName + Assignee.LastName)");
        Console.WriteLine($"            InternalId? (MapIgnore — not mapped)");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 15.1: Multiple Converters on Same DTO
// ═══════════════════════════════════════════════════════════════════════════════

public class SensorReading
{
    public string SensorId { get; set; } = "";
    public double TemperatureCelsius { get; set; }
    public double HumidityPercent { get; set; }
    public double PressureHpa { get; set; }
    public DateTime ReadingTimestamp { get; set; }
    public bool IsActive { get; set; }
}

public class CelsiusToDisplayConverter : IValueConverter<double, string>
{
    public string Convert(double source) => $"{source:F1}°C";
    public double ConvertBack(string target) => double.Parse(target.Replace("°C", ""));
}

public class PercentToBarConverter : IValueConverter<double, string>
{
    public string Convert(double source)
    {
        var bars = (int)(source / 10);
        return $"{"█"[0]}{new string('█', bars)}{new string('░', 10 - bars)} {source:F0}%";
    }

    public double ConvertBack(string target) => double.Parse(target.Split(' ').Last().TrimEnd('%'));
}

public class PressureToDisplayConverter : IValueConverter<double, string>
{
    public string Convert(double source) => $"{source:F1} hPa";
    public double ConvertBack(string target) => double.Parse(target.Replace(" hPa", ""));
}

public class BoolToActiveStatusConverter : IValueConverter<bool, string>
{
    public string Convert(bool source) => source ? "ONLINE" : "OFFLINE";
    public bool ConvertBack(string target) => target == "ONLINE";
}

/// <summary>
///     DTO with 4 different converters + a format string — each property converted differently.
/// </summary>
[MapFrom<SensorReading>]
public partial record SensorDisplayDto
{
    [MapProperty("SensorId")]
    public string Id { get; init; } = "";

    [MapConverter<CelsiusToDisplayConverter>]
    [MapProperty("TemperatureCelsius")]
    public string Temperature { get; init; } = "";

    [MapConverter<PercentToBarConverter>]
    [MapProperty("HumidityPercent")]
    public string Humidity { get; init; } = "";

    [MapConverter<PressureToDisplayConverter>]
    [MapProperty("PressureHpa")]
    public string Pressure { get; init; } = "";

    [MapConverter<BoolToActiveStatusConverter>]
    [MapProperty("IsActive")]
    public string Status { get; init; } = "";

    [MapProperty("ReadingTimestamp", Format = "HH:mm:ss")]
    public string ReadingTime { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 15.2: Bidirectional Converter (Convert + ConvertBack)
// ═══════════════════════════════════════════════════════════════════════════════

public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public int WordCount { get; set; }
}

/// <summary>
///     Converts List&lt;string&gt; to/from comma-separated string.
/// </summary>
public class TagListToCsvConverter : IValueConverter<List<string>, string>
{
    public string Convert(List<string> source) => string.Join(", ", source);

    public List<string> ConvertBack(string target) =>
        target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

/// <summary>
///     Converts word count to readable time estimate and back.
/// </summary>
public class WordCountToReadTimeConverter : IValueConverter<int, string>
{
    public string Convert(int source)
    {
        var minutes = Math.Max(1, source / 200);
        return $"{minutes} min read";
    }

    public int ConvertBack(string target)
    {
        var parts = target.Split(' ');
        return int.TryParse(parts[0], out var minutes) ? minutes * 200 : 0;
    }
}

[MapFrom<BlogPost>]
public partial record BlogPostDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";

    [MapProperty("Tags")]
    [MapConverter<TagListToCsvConverter>]
    public string TagsCsv { get; init; } = "";

    [MapProperty("WordCount")]
    [MapConverter<WordCountToReadTimeConverter>]
    public string ReadTime { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 15.3: Mixed Attributes on One DTO
// ═══════════════════════════════════════════════════════════════════════════════

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}

public class TicketPerson
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int InternalId { get; set; } // Sensitive — should not be mapped
}

public class SupportTicket
{
    public string TicketNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public TicketPriority Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public TicketPerson Reporter { get; set; } = new();
    public TicketPerson Assignee { get; set; } = new();
}

public class PriorityToIconConverter : IValueConverter<TicketPriority, string>
{
    public string Convert(TicketPriority source) => source switch
    {
        TicketPriority.Low => "[LOW]",
        TicketPriority.Medium => "[MED]",
        TicketPriority.High => "[HIGH]",
        TicketPriority.Critical => "[CRIT]",
        _ => "[?]"
    };

    public TicketPriority ConvertBack(string target) => target switch
    {
        "[LOW]" => TicketPriority.Low,
        "[MED]" => TicketPriority.Medium,
        "[HIGH]" => TicketPriority.High,
        "[CRIT]" => TicketPriority.Critical,
        _ => TicketPriority.Low
    };
}

/// <summary>
///     DTO combining every attribute type: MapProperty (rename), MapProperty (nested path),
///     MapProperty (concatenation), MapConverter, MapIgnore, Format — all on one class.
/// </summary>
[MapFrom<SupportTicket>]
public partial record TicketSummaryDto
{
    // Rename: TicketNumber -> Reference
    [MapProperty("TicketNumber")]
    public string Reference { get; init; } = "";

    // Rename: Subject -> Title
    [MapProperty("Subject")]
    public string Title { get; init; } = "";

    // Converter: enum -> display icon
    [MapProperty("Priority")]
    [MapConverter<PriorityToIconConverter>]
    public string PriorityIcon { get; init; } = "";

    // Format on DateTime
    [MapProperty("CreatedAt", Format = "yyyy-MM-dd")]
    public string OpenedDate { get; init; } = "";

    // Concatenation across nested path
    [MapProperty("Reporter.FirstName", "Reporter.LastName")]
    public string ReporterName { get; init; } = "";

    // Nested path: Reporter.Email
    [MapProperty("Reporter.Email")]
    public string ReporterEmail { get; init; } = "";

    // Concatenation across nested path (different source)
    [MapProperty("Assignee.FirstName", "Assignee.LastName")]
    public string AssigneeName { get; init; } = "";

    // MapIgnore: sensitive internal data excluded
    [MapIgnore]
    public int InternalNotes { get; init; }
}
