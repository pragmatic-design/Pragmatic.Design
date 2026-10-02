using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     <c>[Role("manager", "…")]</c>, <c>[IncludesRole&lt;EmployeeRole&gt;]</c> and <c>[Grants(…)]</c> on a partial
///     class: the generator writes the <c>IRole</c> members — <c>Name</c>, <c>Description</c> and a
///     <c>DefaultPermissions</c> flattened through the included roles, de-duplicated and ordered — and the role
///     registry lists the same set.
/// </summary>
/// <remarks>
///     A role was three static members written by hand, and inheriting was a spread of another role's
///     list — which the catalogue could not follow, so the role screen listed a manager as granting only what
///     it added to an employee's.
/// </remarks>
public class ARoleIsDeclaredWithAttributesTests : AuthorizationGeneratorTestBase
{
    private const string Header = """
        using System.Collections.Generic;
        using Pragmatic.Authorization;

        [assembly: Permission("people.team.archive", "Archive a team")]

        namespace TestApp.People;

        """;

    private static string? Registry(SourceGenRunResult result) => GetGeneratedSource(result, "PermissionRegistry");

    [Fact]
    public void TheIRoleMembers_AreGenerated_AndCompile()
    {
        var result = RunGenerator(Header + """
            [Role("employee", "Asks for leave")]
            [Grants("people.leave.ask", TestAssembly.PeoplePermissions.Team.Archive)]
            public partial class EmployeeRole;

            public static class Uses
            {
                public static string Name => EmployeeRole.Name;
                public static IReadOnlyList<string> Grants => EmployeeRole.DefaultPermissions;
            }
            """);

        var members = GetGeneratedSource(result, "EmployeeRole.Role");
        members.Should().NotBeNull("the IRole members are generated");
        members!.Should().Contain("\"employee\"").And.Contain("\"people.team.archive\"");

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
        GetGeneratorDiagnostics(result).Should().BeEmpty("every grant resolves, and the class is partial");
        Registry(result).Should().Contain(
            "new RoleInfo(\"employee\", \"Asks for leave\", [\"people.leave.ask\", \"people.team.archive\"])");
    }

    [Fact]
    public void AnIncludedRole_IsFlattened_DeduplicatedAndOrdered()
    {
        var result = RunGenerator(Header + """
            [Role("employee", "Asks for leave")]
            [Grants("people.b", "people.a")]
            public partial class EmployeeRole;

            [Role("manager", "Decides")]
            [IncludesRole<EmployeeRole>]
            [Grants("people.c", "people.a")]
            public partial class ManagerRole;
            """);

        Registry(result).Should().Contain(
            "new RoleInfo(\"manager\", \"Decides\", [\"people.a\", \"people.b\", \"people.c\"])");
        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
    }

    /// <summary>A hand-written <c>IRole</c> keeps working, and can be included.</summary>
    [Fact]
    public void AHandWrittenRole_IsStillARole_AndCanBeIncluded()
    {
        var result = RunGenerator(Header + """
            public sealed class AuditorRole : IRole
            {
                public static string Name => "auditor";
                public static string? Description => "Reads the audit";
                public static IReadOnlyList<string> DefaultPermissions => ["people.audit.read"];
            }

            [Role("lead", "Leads a team")]
            [IncludesRole<AuditorRole>]
            [Grants("people.lead")]
            public partial class LeadRole;
            """);

        var registry = Registry(result);
        registry.Should().Contain("new RoleInfo(\"auditor\", \"Reads the audit\", [\"people.audit.read\"])");
        registry.Should().Contain("new RoleInfo(\"lead\", \"Leads a team\", [\"people.audit.read\", \"people.lead\"])");
    }

    /// <summary>
    ///     A constant of a referenced assembly beside a generated one. The generated one cannot be bound in this
    ///     run, Roslyn drops every argument with it, and the referenced constant is left as a path the catalogue
    ///     does not hold — while the compiler can fold it on its own.
    /// </summary>
    [Fact]
    public void AReferencedConstantBesideAGeneratedOne_IsItsValue()
    {
        var grants = GeneratorTestHelper.CompileReference("Company.Grants", """
            namespace Company.Grants;

            public static class SharedPermissions
            {
                public const string ChangePassword = "identity.change-password";
            }
            """);

        var result = RunGenerator(Header + """
            [Role("employee", "Asks for leave")]
            [Grants(Company.Grants.SharedPermissions.ChangePassword, TestAssembly.PeoplePermissions.Team.Archive)]
            public partial class EmployeeRole;
            """, grants);

        GetGeneratorDiagnostics(result).Should().BeEmpty();
        Registry(result).Should().Contain(
            "new RoleInfo(\"employee\", \"Asks for leave\", [\"identity.change-password\", \"people.team.archive\"])");
    }

    [Fact]
    public void ACycle_IsReported()
    {
        var result = RunGenerator(Header + """
            [Role("a", "A")]
            [IncludesRole<BRole>]
            public partial class ARole;

            [Role("b", "B")]
            [IncludesRole<ARole>]
            public partial class BRole;
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG1007").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("ARole") && m.Contains("BRole"));
    }

    [Fact]
    public void AGrantThatDoesNotResolve_IsReported()
    {
        var result = RunGenerator(Header + """
            [Role("hr", "Hires")]
            [Grants(PeoplePermissions.Team.Hire)]
            public partial class HrRole;
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG1008").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("PeoplePermissions.Team.Hire"));
    }

    [Fact]
    public void ARoleThatIsNotPartial_IsReported()
    {
        var result = RunGenerator(Header + """
            [Role("x", "X")]
            public sealed class XRole;
            """);

        HasDiagnostic(result, "PRAG1006").Should().BeTrue();
    }

    /// <summary>
    ///     A role of another assembly declared the same way is read from its attributes — they are in its
    ///     metadata, already resolved.
    /// </summary>
    [Fact]
    public void ARoleOfAReferencedAssembly_IsIncludedWithItsGrants()
    {
        var staff = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>("Company.Staff", """
            using Pragmatic.Authorization;

            namespace Company.Staff;

            [Role("staff", "Works here")]
            [Grants("staff.read")]
            public partial class StaffRole;
            """, GetAuthorizationReferences());

        var result = RunGenerator(Header + """
            [Role("lead", "Leads a team")]
            [IncludesRole<Company.Staff.StaffRole>]
            [Grants("staff.approve")]
            public partial class LeadRole;
            """, staff);

        Registry(result).Should().Contain(
            "new RoleInfo(\"lead\", \"Leads a team\", [\"staff.approve\", \"staff.read\"])");
        GetGeneratorDiagnostics(result).Should().BeEmpty();
    }

    /// <summary>A hand-written role of another assembly has no permissions the generator can read: say so.</summary>
    [Fact]
    public void AHandWrittenRoleOfAReferencedAssembly_IsReported()
    {
        var staff = GeneratorTestHelper.CompileReference("Company.Staff", """
            using System.Collections.Generic;
            using Pragmatic.Authorization;

            namespace Company.Staff;

            public sealed class StaffRole : IRole
            {
                public static string Name => "staff";
                public static string? Description => "Works here";
                public static IReadOnlyList<string> DefaultPermissions => ["staff.read"];
            }
            """, GetAuthorizationReferences());

        var result = RunGenerator(Header + """
            [Role("lead", "Leads a team")]
            [IncludesRole<Company.Staff.StaffRole>]
            public partial class LeadRole;
            """, staff);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG1009").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("StaffRole"));
    }
}
