using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates 3-level deep nesting and self-referencing entity mapping with circular reference handling.
/// </summary>
public static class DeepNestingSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("10. Deep Nesting (3 Levels) & Self-Referencing");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowThreeLevelNesting();
        ShowSelfReferencingMapping();

        Console.WriteLine();
    }

    private static void ShowThreeLevelNesting()
    {
        Console.WriteLine("  10.1 Three-Level Nesting: Company -> Departments -> Employees");
        Console.WriteLine("  -------------------------------------------------------------------");

        var company = new Company
        {
            Id = 1,
            Name = "Acme Corp",
            Headquarters = new HeadquartersAddress
            {
                Street = "123 Main St",
                City = "New York",
                Country = "USA"
            },
            Departments =
            [
                new Department
                {
                    Id = 10,
                    Name = "Engineering",
                    Employees =
                    [
                        new CompanyEmployee
                            { Id = 101, FirstName = "Alice", LastName = "Smith", Title = "Senior Engineer" },
                        new CompanyEmployee { Id = 102, FirstName = "Bob", LastName = "Jones", Title = "Tech Lead" }
                    ]
                },
                new Department
                {
                    Id = 20,
                    Name = "Marketing",
                    Employees =
                    [
                        new CompanyEmployee
                            { Id = 201, FirstName = "Carol", LastName = "White", Title = "Marketing Manager" }
                    ]
                }
            ]
        };

        var companyDto = CompanyDto.FromEntity(company);

        Console.WriteLine($"    Company: {companyDto.Name}");
        Console.WriteLine($"    HQ: {companyDto.Headquarters?.City}, {companyDto.Headquarters?.Country}");
        Console.WriteLine($"    Departments ({companyDto.Departments.Count}):");

        foreach (var dept in companyDto.Departments)
        {
            Console.WriteLine($"      - {dept.Name}:");
            foreach (var emp in dept.Employees)
                Console.WriteLine($"          {emp.FullName} ({emp.Title})");
        }

        // Bidirectional round-trip
        var backToEntity = companyDto.ToEntity();
        Console.WriteLine();
        Console.WriteLine($"    Round-trip: {backToEntity.Name} has {backToEntity.Departments.Count} departments");
        Console.WriteLine();
    }

    private static void ShowSelfReferencingMapping()
    {
        Console.WriteLine("  10.2 Self-Referencing: Category Hierarchy (Parent -> Children)");
        Console.WriteLine("  ---------------------------------------------------------------");

        var smartphones = new CategoryNode { Id = 3, Name = "Smartphones", Children = [] };
        var phones = new CategoryNode { Id = 2, Name = "Phones", Children = [smartphones] };
        smartphones.Parent = phones;
        var electronics = new CategoryNode { Id = 1, Name = "Electronics", Children = [phones] };
        phones.Parent = electronics;

        var dto = CategoryNodeDto.FromEntity(electronics);

        Console.WriteLine($"    Root: {dto.Name}");
        PrintCategoryTree(dto, "      ");
        Console.WriteLine();
    }

    private static void PrintCategoryTree(CategoryNodeDto category, string indent)
    {
        foreach (var child in category.Children)
        {
            Console.WriteLine($"{indent}- {child.Name}");
            if (child.Parent != null)
                Console.WriteLine($"{indent}  (parent: {child.Parent.Name})");
            PrintCategoryTree(child, indent + "  ");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 10.1: Three-Level Nesting (Company -> Departments -> Employees)
// ═══════════════════════════════════════════════════════════════════════════════

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public HeadquartersAddress? Headquarters { get; set; }
    public List<Department> Departments { get; set; } = [];
}

public class HeadquartersAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<CompanyEmployee> Employees { get; set; } = [];
}

public class CompanyEmployee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Title { get; set; } = "";
}

[MapFrom<Company>]
[MapTo<Company>]
public partial record CompanyDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public HeadquartersAddressDto? Headquarters { get; init; }
    public List<DepartmentDto> Departments { get; init; } = [];
}

[MapFrom<HeadquartersAddress>]
[MapTo<HeadquartersAddress>]
public partial record HeadquartersAddressDto
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
}

[MapFrom<Department>]
[MapTo<Department>]
public partial record DepartmentDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public List<CompanyEmployeeDto> Employees { get; init; } = [];
}

[MapFrom<CompanyEmployee>]
[MapTo<CompanyEmployee>]
public partial record CompanyEmployeeDto
{
    public int Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Title { get; init; } = "";
    [MapIgnore] public string FullName => $"{FirstName} {LastName}";
}

// ═══════════════════════════════════════════════════════════════════════════════
// 10.2: Self-Referencing (Category Tree)
// ═══════════════════════════════════════════════════════════════════════════════

public class CategoryNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CategoryNode? Parent { get; set; }
    public List<CategoryNode> Children { get; set; } = [];
}

[MapFrom<CategoryNode>]
public partial record CategoryNodeDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CategoryNodeDto? Parent { get; init; }
    public List<CategoryNodeDto> Children { get; init; } = [];
}
