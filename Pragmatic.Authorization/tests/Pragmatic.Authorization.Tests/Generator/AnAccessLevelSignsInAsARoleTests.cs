using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     <c>[SignsInAs&lt;TRole&gt;]</c> on the members of an enum a user entity keeps its access level in: the generator
///     writes the enum-to-role mapping as a switch over every member, and a member without one is an error.
/// </summary>
/// <remarks>
///     Time off mapped its <c>AccessRole</c> to a role name with a hand-written switch nothing checked: a
///     new member without a case compiled, and threw <c>ArgumentOutOfRangeException</c> at the sign-in of the first
///     employee who had it.
/// </remarks>
public class AnAccessLevelSignsInAsARoleTests : AuthorizationGeneratorTestBase
{
    private const string Roles = """
        using System.Collections.Generic;
        using Pragmatic.Authorization;

        namespace TestApp.People;

        [Role("employee", "Asks for leave")]
        public partial class EmployeeRole;

        [Role("manager", "Decides leave")]
        public partial class ManagerRole;

        public sealed class AuditorRole : IRole
        {
            public static string Name => "auditor";
            public static string? Description => "Reads the audit";
            public static IReadOnlyList<string> DefaultPermissions => [];
        }

        """;

    [Fact]
    public void EveryMember_IsMappedToItsRolesName()
    {
        var result = RunGenerator(Roles + """
            public enum AccessLevel
            {
                [SignsInAs<EmployeeRole>] Employee,
                [SignsInAs<ManagerRole>] Manager,
                [SignsInAs<AuditorRole>] Auditor
            }

            public static class Uses
            {
                public static string Of(AccessLevel level) => level.RoleNameOf();
            }
            """);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
        GetGeneratorDiagnostics(result).Should().BeEmpty();

        var mapping = GetGeneratedSource(result, "AccessLevelRoleNames")!;
        mapping.Should().Contain("public static class AccessLevelRoleNames");
        mapping.Should().Contain("global::TestApp.People.AccessLevel.Employee => global::TestApp.People.EmployeeRole.Name");
        mapping.Should().Contain("global::TestApp.People.AccessLevel.Manager => global::TestApp.People.ManagerRole.Name");
        mapping.Should().Contain("global::TestApp.People.AccessLevel.Auditor => global::TestApp.People.AuditorRole.Name",
            "a hand-written IRole signs in as well as a declared one");
    }

    [Fact]
    public void AMemberWithoutARole_IsReported()
    {
        var result = RunGenerator(Roles + """
            public enum AccessLevel
            {
                [SignsInAs<EmployeeRole>] Employee,
                [SignsInAs<ManagerRole>] Manager,
                Contractor
            }
            """);

        var reported = GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG1014").ToList();
        reported.Should().ContainSingle("a member with no role would throw at the sign-in of whoever has it");
        reported[0].GetMessage().Should().Contain("Contractor").And.Contain("AccessLevel");
        reported[0].Location.GetLineSpan().StartLinePosition.Line.Should().BeGreaterThan(0, "it is reported on the member");
    }

    /// <summary>The control: an enum no member of which signs in as anything generates nothing.</summary>
    [Fact]
    public void AnEnumWithoutTheAttribute_GeneratesNothing()
    {
        var result = RunGenerator(Roles + """
            public enum Colour { Red, Green }
            """);

        GetGeneratedSource(result, "RoleNames").Should().BeNull();
        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG1014").Should().BeEmpty();
    }
}
