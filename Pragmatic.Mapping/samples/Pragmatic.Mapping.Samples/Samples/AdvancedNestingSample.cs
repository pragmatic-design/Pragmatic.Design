using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Advanced nesting scenarios: 4-level deep, nullable intermediate levels,
///     and self-referencing round-trip (MapFrom + MapTo on trees).
/// </summary>
public static class AdvancedNestingSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("14. Advanced Nesting — 4 Levels, Nullable, Round-Trip");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowFourLevelNesting();
        ShowNullableIntermediateLevels();
        ShowSelfReferencingRoundTrip();

        Console.WriteLine();
    }

    private static void ShowFourLevelNesting()
    {
        Console.WriteLine("  14.1 Four-Level Nesting: Organization -> Divisions -> Teams -> Members");
        Console.WriteLine("  ----------------------------------------------------------------------");

        var org = new Organization
        {
            Id = 1,
            Name = "TechCorp Global",
            Divisions =
            [
                new Division
                {
                    Id = 10,
                    Name = "Product",
                    Teams =
                    [
                        new OrgTeam
                        {
                            Id = 100,
                            Name = "Platform",
                            Members =
                            [
                                new OrgMember { Id = 1001, Name = "Alice", Role = "Lead" },
                                new OrgMember { Id = 1002, Name = "Bob", Role = "Engineer" }
                            ]
                        },
                        new OrgTeam
                        {
                            Id = 101,
                            Name = "Mobile",
                            Members = [new OrgMember { Id = 1003, Name = "Carol", Role = "Engineer" }]
                        }
                    ]
                }
            ]
        };

        var orgDto = OrganizationDto.FromEntity(org);

        Console.WriteLine($"    Org: {orgDto.Name}");
        foreach (var div in orgDto.Divisions)
        {
            Console.WriteLine($"      Division: {div.Name}");
            foreach (var team in div.Teams)
            {
                Console.WriteLine($"        Team: {team.Name}");
                foreach (var member in team.Members)
                    Console.WriteLine($"          - {member.Name} ({member.Role})");
            }
        }

        // 4-level round-trip
        var backToEntity = orgDto.ToEntity();
        var firstMember = backToEntity.Divisions[0].Teams[0].Members[0];
        Console.WriteLine();
        Console.WriteLine($"    Round-trip: {backToEntity.Name} -> {backToEntity.Divisions[0].Name} -> " +
                          $"{backToEntity.Divisions[0].Teams[0].Name} -> {firstMember.Name} ({firstMember.Role})");
        Console.WriteLine();
    }

    private static void ShowNullableIntermediateLevels()
    {
        Console.WriteLine("  14.2 Nullable Intermediate Levels: Supplier -> Warehouse? -> Location?");
        Console.WriteLine("  -----------------------------------------------------------------------");

        var supplierFull = new Supplier
        {
            Id = 1,
            SupplierName = "Full Supply Co",
            MainWarehouse = new Warehouse
            {
                Id = 10,
                Name = "Central Hub",
                Location = new GeoLocation { Latitude = 40.7128, Longitude = -74.0060 }
            }
        };

        var fullDto = SupplierDto.FromEntity(supplierFull);
        Console.WriteLine($"    Full:     {fullDto.SupplierName}");
        Console.WriteLine($"              Warehouse: {fullDto.MainWarehouse?.Name}");
        Console.WriteLine($"              Location:  ({fullDto.MainWarehouse?.Location?.Latitude}, {fullDto.MainWarehouse?.Location?.Longitude})");

        var supplierNoWarehouse = new Supplier { Id = 2, SupplierName = "Small Supplier", MainWarehouse = null };
        var noWarehouseDto = SupplierDto.FromEntity(supplierNoWarehouse);
        Console.WriteLine($"    No WH:    {noWarehouseDto.SupplierName}");
        Console.WriteLine($"              Warehouse: {(noWarehouseDto.MainWarehouse == null ? "(null)" : noWarehouseDto.MainWarehouse.Name)}");

        var supplierNoLocation = new Supplier
        {
            Id = 3,
            SupplierName = "Warehouse Inc",
            MainWarehouse = new Warehouse { Id = 20, Name = "Unnamed WH", Location = null }
        };
        var noLocationDto = SupplierDto.FromEntity(supplierNoLocation);
        Console.WriteLine($"    No Loc:   {noLocationDto.SupplierName}");
        Console.WriteLine($"              Warehouse: {noLocationDto.MainWarehouse?.Name}");
        Console.WriteLine($"              Location:  {(noLocationDto.MainWarehouse?.Location == null ? "(null)" : "present")}");
        Console.WriteLine();
    }

    private static void ShowSelfReferencingRoundTrip()
    {
        Console.WriteLine("  14.3 Self-Referencing Round-Trip: DTO -> Entity (MapTo)");
        Console.WriteLine("  -------------------------------------------------------");

        var menuDto = new MenuItemDto
        {
            Id = 1,
            Label = "File",
            SubItems =
            [
                new MenuItemDto
                {
                    Id = 2,
                    Label = "New",
                    SubItems =
                    [
                        new MenuItemDto { Id = 3, Label = "Project", SubItems = [] },
                        new MenuItemDto { Id = 4, Label = "File", SubItems = [] }
                    ]
                },
                new MenuItemDto { Id = 5, Label = "Open", SubItems = [] },
                new MenuItemDto { Id = 6, Label = "Exit", SubItems = [] }
            ]
        };

        var menuEntity = menuDto.ToEntity();

        Console.WriteLine($"    Menu: {menuEntity.Label}");
        foreach (var child in menuEntity.SubItems)
        {
            Console.WriteLine($"      - {child.Label}");
            foreach (var grandchild in child.SubItems)
                Console.WriteLine($"          - {grandchild.Label}");
        }

        var roundTripDto = MenuItemDto.FromEntity(menuEntity);
        Console.WriteLine();
        Console.WriteLine($"    Round-trip: {roundTripDto.Label} has {roundTripDto.SubItems.Count} children, " +
                          $"first child \"{roundTripDto.SubItems[0].Label}\" has {roundTripDto.SubItems[0].SubItems.Count} sub-items");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 14.1: Four-Level Nesting (Organization -> Divisions -> Teams -> Members)
// ═══════════════════════════════════════════════════════════════════════════════

public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Division> Divisions { get; set; } = [];
}

public class Division
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<OrgTeam> Teams { get; set; } = [];
}

public class OrgTeam
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<OrgMember> Members { get; set; } = [];
}

public class OrgMember
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

[MapFrom<Organization>]
[MapTo<Organization>]
public partial record OrganizationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public List<DivisionDto> Divisions { get; init; } = [];
}

[MapFrom<Division>]
[MapTo<Division>]
public partial record DivisionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public List<OrgTeamDto> Teams { get; init; } = [];
}

[MapFrom<OrgTeam>]
[MapTo<OrgTeam>]
public partial record OrgTeamDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public List<OrgMemberDto> Members { get; init; } = [];
}

[MapFrom<OrgMember>]
[MapTo<OrgMember>]
public partial record OrgMemberDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 14.2: Nullable Intermediate Levels
// ═══════════════════════════════════════════════════════════════════════════════

public class GeoLocation
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class Warehouse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public GeoLocation? Location { get; set; }
}

public class Supplier
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = "";
    public Warehouse? MainWarehouse { get; set; }
}

[MapFrom<GeoLocation>]
public partial record GeoLocationDto
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

[MapFrom<Warehouse>]
public partial record WarehouseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public GeoLocationDto? Location { get; init; }
}

[MapFrom<Supplier>]
public partial record SupplierDto
{
    public int Id { get; init; }
    public string SupplierName { get; init; } = "";
    public WarehouseDto? MainWarehouse { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 14.3: Self-Referencing Round-Trip (Menu hierarchy)
// ═══════════════════════════════════════════════════════════════════════════════

public class MenuItem
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public List<MenuItem> SubItems { get; set; } = [];
}

[MapFrom<MenuItem>]
[MapTo<MenuItem>]
public partial record MenuItemDto
{
    public int Id { get; init; }
    public string Label { get; init; } = "";
    public List<MenuItemDto> SubItems { get; init; } = [];
}
