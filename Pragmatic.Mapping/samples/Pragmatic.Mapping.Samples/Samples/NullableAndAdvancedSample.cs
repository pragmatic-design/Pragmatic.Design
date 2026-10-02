using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates nullable handling, collection conversions, and circular references.
/// </summary>
public static class NullableAndAdvancedSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Nullable & Advanced Mapping Samples");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowAutoDefaultMapping();
        ShowCollectionTypeConversions();
        ShowCircularReferenceHandling();
        ShowDateOnlyToDateTimeConversion();

        Console.WriteLine();
    }

    private static void ShowAutoDefaultMapping()
    {
        Console.WriteLine("  7.1 Auto-Default (Nullable -> Non-Nullable)");
        Console.WriteLine("  --------------------------------------------");

        var entity = new EntityWithNullables
        {
            Id = 1,
            Name = null, // Will become ""
            Quantity = null, // Will become 0
            Price = null, // Will become 0m
            CreatedAt = null, // Will become DateTime.MinValue
            ExternalId = null // Will become Guid.Empty
        };

        var dto = NonNullableDto.FromEntity(entity);

        Console.WriteLine(
            $"    Entity:  Name={entity.Name ?? "(null)"}, Qty={entity.Quantity?.ToString() ?? "(null)"}, Price={entity.Price?.ToString() ?? "(null)"}");
        Console.WriteLine($"    DTO:     Name=\"{dto.Name}\", Qty={dto.Quantity}, Price={dto.Price}");
        Console.WriteLine($"    Guid:    {entity.ExternalId?.ToString() ?? "(null)"} -> {dto.ExternalId}");
        Console.WriteLine();
    }

    private static void ShowCollectionTypeConversions()
    {
        Console.WriteLine("  7.2 Collection Type Conversions");
        Console.WriteLine("  --------------------------------");

        var entity = new EntityWithCollections
        {
            Id = 1,
            TagsList = ["c#", "dotnet", "mapping"],
            ValuesArray = [1, 2, 3, 4, 5],
            UniqueItems = ["apple", "banana", "cherry"]
        };

        var dto = CollectionConversionDto.FromEntity(entity);

        Console.WriteLine($"    Source List<string>:   [{string.Join(", ", entity.TagsList)}]");
        Console.WriteLine($"    Target string[]:       [{string.Join(", ", dto.TagsList)}] (via .ToArray())");
        Console.WriteLine();
        Console.WriteLine($"    Source int[]:          [{string.Join(", ", entity.ValuesArray)}]");
        Console.WriteLine($"    Target List<int>:      [{string.Join(", ", dto.ValuesArray)}] (via .ToList())");
        Console.WriteLine();
        Console.WriteLine($"    Source List<string>:   [{string.Join(", ", entity.UniqueItems)}]");
        Console.WriteLine($"    Target HashSet:        [{string.Join(", ", dto.UniqueItems)}] (via .ToHashSet())");
        Console.WriteLine();
    }

    private static void ShowCircularReferenceHandling()
    {
        Console.WriteLine("  7.3 Circular Reference Handling");
        Console.WriteLine("  --------------------------------");

        // Create circular reference: User -> Team -> Leader (User)
        var leader = new TeamMember
        {
            Id = 1,
            Name = "Alice (Leader)"
        };

        var team = new Team
        {
            Id = 100,
            Name = "Engineering",
            Leader = leader
        };

        leader.Team = team; // Circular reference!

        var member = new TeamMember
        {
            Id = 2,
            Name = "Bob",
            Team = team
        };

        // This would cause infinite recursion without proper handling
        var memberDto = TeamMemberDto.FromEntity(member);

        Console.WriteLine($"    Member: {memberDto.Name}");
        Console.WriteLine($"    Team:   {memberDto.Team?.Name}");
        Console.WriteLine($"    Leader: {memberDto.Team?.Leader?.Name}");
        Console.WriteLine(
            $"    Leader's Team: {(memberDto.Team?.Leader?.Team != null ? memberDto.Team.Leader.Team.Name : "(stopped - circular ref)")}");
        Console.WriteLine();
    }

    private static void ShowDateOnlyToDateTimeConversion()
    {
        Console.WriteLine("  7.4 DateOnly -> DateTime Conversion");
        Console.WriteLine("  ------------------------------------");

        var booking = new BookingDto
        {
            Id = 1,
            GuestName = "John Doe",
            CheckInDate = new DateOnly(2025, 6, 15),
            CheckOutDate = new DateOnly(2025, 6, 20)
        };

        var entity = BookingEntity.FromEntity(booking);

        Console.WriteLine($"    DTO CheckIn (DateOnly):     {booking.CheckInDate}");
        Console.WriteLine($"    Entity CheckIn (DateTime):  {entity.CheckInDate}");
        Console.WriteLine($"    DTO CheckOut (DateOnly):    {booking.CheckOutDate}");
        Console.WriteLine($"    Entity CheckOut (DateTime): {entity.CheckOutDate}");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 7.1: Auto-Default (Nullable -> Non-Nullable)
// ═══════════════════════════════════════════════════════════════════════════════

public class EntityWithNullables
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int? Quantity { get; set; }
    public decimal? Price { get; set; }
    public DateTime? CreatedAt { get; set; }
    public Guid? ExternalId { get; set; }
}

/// <summary>
///     DTO with non-nullable properties - auto-default is applied.
/// </summary>
[MapFrom<EntityWithNullables>]
public partial record NonNullableDto
{
    public int Id { get; init; }
    public string Name { get; init; } = ""; // string? -> string via ?? ""
    public int Quantity { get; init; } // int? -> int via GetValueOrDefault()
    public decimal Price { get; init; } // decimal? -> decimal via GetValueOrDefault()
    public DateTime CreatedAt { get; init; } // DateTime? -> DateTime via GetValueOrDefault()
    public Guid ExternalId { get; init; } // Guid? -> Guid via GetValueOrDefault()
}

// ═══════════════════════════════════════════════════════════════════════════════
// 7.2: Collection Type Conversions
// ═══════════════════════════════════════════════════════════════════════════════

public class EntityWithCollections
{
    public int Id { get; set; }
    public List<string> TagsList { get; set; } = [];
    public int[] ValuesArray { get; set; } = [];
    public List<string> UniqueItems { get; set; } = [];
}

/// <summary>
///     DTO with different collection types - automatic conversion.
/// </summary>
[MapFrom<EntityWithCollections>]
public partial record CollectionConversionDto
{
    public int Id { get; init; }
    public string[] TagsList { get; init; } = []; // List<T> -> T[] via .ToArray()
    public List<int> ValuesArray { get; init; } = []; // T[] -> List<T> via .ToList()
    public HashSet<string> UniqueItems { get; init; } = []; // List<T> -> HashSet<T> via .ToHashSet()
}

// ═══════════════════════════════════════════════════════════════════════════════
// 7.3: Circular Reference Handling
// ═══════════════════════════════════════════════════════════════════════════════

public class TeamMember
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Team? Team { get; set; } // -> Team -> Leader -> Team (circular!)
}

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public TeamMember? Leader { get; set; } // -> TeamMember -> Team (circular!)
}

[MapFrom<TeamMember>]
public partial record TeamMemberDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public TeamDto? Team { get; init; }
}

[MapFrom<Team>]
public partial record TeamDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public TeamMemberDto? Leader { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 7.4: DateOnly -> DateTime Conversion
// ═══════════════════════════════════════════════════════════════════════════════

public class BookingDto
{
    public int Id { get; set; }
    public string GuestName { get; set; } = "";
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
}

/// <summary>
///     Entity with DateTime - DateOnly.ToDateTime(TimeOnly.MinValue) is used.
/// </summary>
[MapFrom<BookingDto>]
public partial record BookingEntity
{
    public int Id { get; init; }
    public string GuestName { get; init; } = "";
    public DateTime CheckInDate { get; init; } // DateOnly -> DateTime
    public DateTime CheckOutDate { get; init; } // DateOnly -> DateTime
}