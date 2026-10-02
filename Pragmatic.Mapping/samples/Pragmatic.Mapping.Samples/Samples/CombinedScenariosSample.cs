using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Converters;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Combined real-world scenarios: concatenation across nested paths,
///     converter + MapProperty on nested paths, and bidirectional with renames + TargetPath.
/// </summary>
public static class CombinedScenariosSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("17. Concatenation, Nested Converter & Bidirectional Renames");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowConcatenationAcrossNested();
        ShowConverterOnNestedPath();
        ShowBidirectionalWithRenames();

        Console.WriteLine();
    }

    private static void ShowConcatenationAcrossNested()
    {
        Console.WriteLine("  17.1 Concatenation — Combine properties from different nested paths");
        Console.WriteLine("  -------------------------------------------------------------------");

        var delivery = new DeliveryRecord
        {
            Id = 100,
            RecipientFirstName = "Marco",
            RecipientLastName = "Verdi",
            Address = new DeliveryAddress
            {
                Street = "Via Garibaldi 10",
                City = "Roma",
                PostalCode = "00100",
                Country = "Italia"
            },
            ScheduledDate = new DateTime(2025, 6, 20, 14, 0, 0)
        };

        var dto = DeliveryNotificationDto.FromEntity(delivery);

        Console.WriteLine($"    Entity: FirstName=\"{delivery.RecipientFirstName}\", LastName=\"{delivery.RecipientLastName}\"");
        Console.WriteLine($"            Street=\"{delivery.Address.Street}\", City=\"{delivery.Address.City}\"");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    RecipientFullName=\"{dto.RecipientFullName}\"");
        Console.WriteLine($"            (concatenation: FirstName + \" \" + LastName)");
        Console.WriteLine();
        Console.WriteLine($"            AddressLine=\"{dto.AddressLine}\"");
        Console.WriteLine($"            (concatenation with \", \" separator: Street, PostalCode, City)");
        Console.WriteLine();
        Console.WriteLine($"            DeliveryDate=\"{dto.DeliveryDate}\" (format: yyyy-MM-dd)");
        Console.WriteLine();
    }

    private static void ShowConverterOnNestedPath()
    {
        Console.WriteLine("  17.2 Converter + MapProperty on Nested Path");
        Console.WriteLine("  ----------------------------------------------");

        var flight = new FlightEntity
        {
            FlightNumber = "AZ-1234",
            Departure = new AirportInfo
            {
                IataCode = "FCO",
                Name = "Fiumicino",
                ScheduledTime = new DateTime(2025, 7, 1, 8, 30, 0)
            },
            Arrival = new AirportInfo
            {
                IataCode = "JFK",
                Name = "John F. Kennedy",
                ScheduledTime = new DateTime(2025, 7, 1, 14, 45, 0)
            },
            DistanceKm = 6880
        };

        var dto = FlightCardDto.FromEntity(flight);

        Console.WriteLine($"    Entity: FlightNumber=\"{flight.FlightNumber}\", DistanceKm={flight.DistanceKm}");
        Console.WriteLine($"            Departure.IataCode=\"{flight.Departure.IataCode}\"");
        Console.WriteLine($"            Arrival.ScheduledTime={flight.Arrival.ScheduledTime}");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    Flight=\"{dto.Flight}\" (from FlightNumber)");
        Console.WriteLine($"            Origin=\"{dto.Origin}\" (from Departure.IataCode — nested path)");
        Console.WriteLine($"            Destination=\"{dto.Destination}\" (from Arrival.IataCode — nested path)");
        Console.WriteLine($"            DepartureTime=\"{dto.DepartureTime}\" (from Departure.ScheduledTime, HH:mm format)");
        Console.WriteLine($"            ArrivalTime=\"{dto.ArrivalTime}\" (from Arrival.ScheduledTime, HH:mm format)");
        Console.WriteLine($"            Distance=\"{dto.Distance}\" (KmToDisplayConverter on DistanceKm)");
        Console.WriteLine();
    }

    private static void ShowBidirectionalWithRenames()
    {
        Console.WriteLine("  17.3 Bidirectional with Renames — MapFrom + MapTo + TargetPath");
        Console.WriteLine("  ---------------------------------------------------------------");

        var contract = new ContractEntity
        {
            ContractId = 1,
            ContractTitle = "Annual Support",
            ContractValue = 50000m,
            ContractCurrency = "EUR",
            Signatory = new ContractPerson { FullName = "Luigi Bianchi", Role = "CTO" }
        };

        var readDto = ContractDto.FromEntity(contract);

        Console.WriteLine($"    Entity -> DTO:");
        Console.WriteLine($"      ContractTitle=\"{contract.ContractTitle}\" -> Title=\"{readDto.Title}\"");
        Console.WriteLine($"      ContractValue={contract.ContractValue} -> Amount={readDto.Amount}");
        Console.WriteLine($"      Signatory.FullName=\"{contract.Signatory.FullName}\" -> SignatoryName=\"{readDto.SignatoryName}\"");

        var writeDto = new ContractDto
        {
            Id = 2,
            Title = "New Partnership",
            Amount = 100000m,
            Currency = "USD",
            SignatoryName = "John Smith",
            SignatoryRole = "CEO"
        };

        var newEntity = writeDto.ToEntity();

        Console.WriteLine();
        Console.WriteLine($"    DTO -> Entity (via Target paths):");
        Console.WriteLine($"      Title=\"{writeDto.Title}\" -> ContractTitle=\"{newEntity.ContractTitle}\"");
        Console.WriteLine($"      Amount={writeDto.Amount} -> ContractValue={newEntity.ContractValue}");
        Console.WriteLine($"      SignatoryName=\"{writeDto.SignatoryName}\" -> Signatory.FullName=\"{newEntity.Signatory.FullName}\"");
        Console.WriteLine($"      SignatoryRole=\"{writeDto.SignatoryRole}\" -> Signatory.Role=\"{newEntity.Signatory.Role}\"");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 17.1: Concatenation Across Nested Objects
// ═══════════════════════════════════════════════════════════════════════════════

public class DeliveryAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public class DeliveryRecord
{
    public int Id { get; set; }
    public string RecipientFirstName { get; set; } = "";
    public string RecipientLastName { get; set; } = "";
    public DeliveryAddress Address { get; set; } = new();
    public DateTime ScheduledDate { get; set; }
}

[MapFrom<DeliveryRecord>]
public partial record DeliveryNotificationDto
{
    public int Id { get; init; }

    [MapProperty("RecipientFirstName", "RecipientLastName")]
    public string RecipientFullName { get; init; } = "";

    [MapProperty("Address.Street", "Address.PostalCode", "Address.City", Separator = ", ")]
    public string AddressLine { get; init; } = "";

    [MapProperty("ScheduledDate", Format = "yyyy-MM-dd")]
    public string DeliveryDate { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 17.2: Converter on Nested Path
// ═══════════════════════════════════════════════════════════════════════════════

public class AirportInfo
{
    public string IataCode { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime ScheduledTime { get; set; }
}

public class FlightEntity
{
    public string FlightNumber { get; set; } = "";
    public AirportInfo Departure { get; set; } = new();
    public AirportInfo Arrival { get; set; } = new();
    public int DistanceKm { get; set; }
}

public class KmToDisplayConverter : IValueConverter<int, string>
{
    public string Convert(int source) => $"{source:N0} km";
    public int ConvertBack(string target) => int.Parse(target.Replace(" km", "").Replace(",", "").Replace(".", ""));
}

[MapFrom<FlightEntity>]
public partial record FlightCardDto
{
    [MapProperty("FlightNumber")]
    public string Flight { get; init; } = "";

    [MapProperty("Departure.IataCode")]
    public string Origin { get; init; } = "";

    [MapProperty("Arrival.IataCode")]
    public string Destination { get; init; } = "";

    [MapProperty("Departure.ScheduledTime", Format = "HH:mm")]
    public string DepartureTime { get; init; } = "";

    [MapProperty("Arrival.ScheduledTime", Format = "HH:mm")]
    public string ArrivalTime { get; init; } = "";

    [MapProperty("DistanceKm")]
    [MapConverter<KmToDisplayConverter>]
    public string Distance { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 17.3: Bidirectional with Renames + TargetPath
// ═══════════════════════════════════════════════════════════════════════════════

public class ContractPerson
{
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
}

public class ContractEntity
{
    public int ContractId { get; set; }
    public string ContractTitle { get; set; } = "";
    public decimal ContractValue { get; set; }
    public string ContractCurrency { get; set; } = "";
    public ContractPerson Signatory { get; set; } = new();
}

[MapFrom<ContractEntity>]
[MapTo<ContractEntity>]
public partial record ContractDto
{
    [MapProperty("ContractId", Target = "ContractId")]
    public int Id { get; init; }

    [MapProperty("ContractTitle", Target = "ContractTitle")]
    public string Title { get; init; } = "";

    [MapProperty("ContractValue", Target = "ContractValue")]
    public decimal Amount { get; init; }

    [MapProperty("ContractCurrency", Target = "ContractCurrency")]
    public string Currency { get; init; } = "";

    [MapProperty("Signatory.FullName", Target = "Signatory.FullName")]
    public string SignatoryName { get; init; } = "";

    [MapProperty("Signatory.Role", Target = "Signatory.Role")]
    public string SignatoryRole { get; init; } = "";
}
