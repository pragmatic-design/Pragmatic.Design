using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Real-world mapping with property name mismatches and multi-level flattening.
///     Every DTO intentionally uses different names than the entity.
/// </summary>
public static class RealWorldMappingSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("16. Property Mismatch & Multi-Level Flattening");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowPropertyNameMismatch();
        ShowMultiLevelFlattening();

        Console.WriteLine();
    }

    private static void ShowPropertyNameMismatch()
    {
        Console.WriteLine("  16.1 Property Name Mismatch — [MapProperty] resolves different names");
        Console.WriteLine("  -------------------------------------------------------------------");

        var patient = new PatientRecord
        {
            RecordId = 42,
            GivenName = "Maria",
            FamilyName = "Rossi",
            DateOfBirth = new DateTime(1985, 3, 15),
            SocialSecurityNumber = "RSSMRA85C55F205Z",
            PrimaryCarePhysician = "Dr. Bianchi"
        };

        var dto = PatientSummaryDto.FromEntity(patient);

        Console.WriteLine($"    Entity: GivenName=\"{patient.GivenName}\", FamilyName=\"{patient.FamilyName}\"");
        Console.WriteLine($"            DateOfBirth={patient.DateOfBirth:yyyy-MM-dd}, SSN=\"{patient.SocialSecurityNumber}\"");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    FirstName=\"{dto.FirstName}\" (from GivenName)");
        Console.WriteLine($"            LastName=\"{dto.LastName}\" (from FamilyName)");
        Console.WriteLine($"            BirthDate=\"{dto.BirthDate}\" (from DateOfBirth, formatted)");
        Console.WriteLine($"            TaxCode=\"{dto.TaxCode}\" (from SocialSecurityNumber)");
        Console.WriteLine($"            Doctor=\"{dto.Doctor}\" (from PrimaryCarePhysician)");
        Console.WriteLine();
    }

    private static void ShowMultiLevelFlattening()
    {
        Console.WriteLine("  16.2 Multi-Level Flattening — deep path (2-4 levels) -> flat DTO");
        Console.WriteLine("  -----------------------------------------------------------------");

        var project = new ProjectEntity
        {
            Id = 1,
            Code = "PRJ-ALPHA",
            Lead = new ProjectLead
            {
                Name = "Alice Johnson",
                Contact = new ContactInfo
                {
                    Email = "alice@company.com",
                    Phone = "+39 02 1234567",
                    Office = new OfficeLocation { Building = "Tower A", Floor = 12, Room = "12-B" }
                }
            },
            Client = new ClientInfo
            {
                CompanyName = "TechCorp",
                BillingContact = new ContactInfo
                {
                    Email = "billing@techcorp.com",
                    Phone = "+1 555 0100",
                    Office = new OfficeLocation { Building = "HQ", Floor = 1, Room = "Reception" }
                }
            }
        };

        var dto = ProjectFlatDto.FromEntity(project);

        Console.WriteLine($"    Entity: project.Lead.Contact.Email = \"{project.Lead.Contact.Email}\"");
        Console.WriteLine($"            project.Lead.Contact.Office.Building = \"{project.Lead.Contact.Office.Building}\"");
        Console.WriteLine($"            project.Client.CompanyName = \"{project.Client.CompanyName}\"");
        Console.WriteLine();
        Console.WriteLine($"    DTO:    ProjectCode=\"{dto.ProjectCode}\" (from Code)");
        Console.WriteLine($"            LeadName=\"{dto.LeadName}\" (from Lead.Name — 2 levels)");
        Console.WriteLine($"            LeadEmail=\"{dto.LeadEmail}\" (from Lead.Contact.Email — 3 levels)");
        Console.WriteLine($"            LeadPhone=\"{dto.LeadPhone}\" (from Lead.Contact.Phone — 3 levels)");
        Console.WriteLine($"            LeadBuilding=\"{dto.LeadBuilding}\" (from Lead.Contact.Office.Building — 4 levels)");
        Console.WriteLine($"            LeadFloor={dto.LeadFloor} (from Lead.Contact.Office.Floor — 4 levels)");
        Console.WriteLine($"            ClientName=\"{dto.ClientName}\" (from Client.CompanyName — 2 levels)");
        Console.WriteLine($"            ClientEmail=\"{dto.ClientEmail}\" (from Client.BillingContact.Email — 3 levels)");
        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 16.1: Property Name Mismatch
// ═══════════════════════════════════════════════════════════════════════════════

public class PatientRecord
{
    public int RecordId { get; set; }
    public string GivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public DateTime DateOfBirth { get; set; }
    public string SocialSecurityNumber { get; set; } = "";
    public string PrimaryCarePhysician { get; set; } = "";
}

[MapFrom<PatientRecord>]
public partial record PatientSummaryDto
{
    [MapProperty("RecordId")]
    public int Id { get; init; }

    [MapProperty("GivenName")]
    public string FirstName { get; init; } = "";

    [MapProperty("FamilyName")]
    public string LastName { get; init; } = "";

    [MapProperty("DateOfBirth", Format = "yyyy-MM-dd")]
    public string BirthDate { get; init; } = "";

    [MapProperty("SocialSecurityNumber")]
    public string TaxCode { get; init; } = "";

    [MapProperty("PrimaryCarePhysician")]
    public string Doctor { get; init; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 16.2: Multi-Level Flattening (4 levels deep)
// ═══════════════════════════════════════════════════════════════════════════════

public class OfficeLocation
{
    public string Building { get; set; } = "";
    public int Floor { get; set; }
    public string Room { get; set; } = "";
}

public class ContactInfo
{
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public OfficeLocation Office { get; set; } = new();
}

public class ProjectLead
{
    public string Name { get; set; } = "";
    public ContactInfo Contact { get; set; } = new();
}

public class ClientInfo
{
    public string CompanyName { get; set; } = "";
    public ContactInfo BillingContact { get; set; } = new();
}

public class ProjectEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public ProjectLead Lead { get; set; } = new();
    public ClientInfo Client { get; set; } = new();
}

[MapFrom<ProjectEntity>]
public partial record ProjectFlatDto
{
    public int Id { get; init; }

    [MapProperty("Code")]
    public string ProjectCode { get; init; } = "";

    [MapProperty("Lead.Name")]
    public string LeadName { get; init; } = "";

    [MapProperty("Lead.Contact.Email")]
    public string LeadEmail { get; init; } = "";

    [MapProperty("Lead.Contact.Phone")]
    public string LeadPhone { get; init; } = "";

    [MapProperty("Lead.Contact.Office.Building")]
    public string LeadBuilding { get; init; } = "";

    [MapProperty("Lead.Contact.Office.Floor")]
    public int LeadFloor { get; init; }

    [MapProperty("Client.CompanyName")]
    public string ClientName { get; init; } = "";

    [MapProperty("Client.BillingContact.Email")]
    public string ClientEmail { get; init; } = "";
}
