using System.Linq;
using System.Text.RegularExpressions;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Identity;

/// <summary>
///     <c>[assembly: Permission("people.personal-data.erase", "…", Category = "…")]</c>: a custom permission is
///     a line, its constant a <c>const</c> of the one <c>{Boundary}Permissions</c> class the CRUD permissions
///     are in, and its name, description and category an entry of the permission registry.
/// </summary>
/// <remarks>
///     There is no <c>IPermission</c> class to write: the attribute is the one way. The generator writes
///     one <c>{Boundary}Permissions</c>, the <c>const</c> one every caller names — never a second,
///     <c>static readonly</c> copy, which would be unusable in an attribute.
/// </remarks>
public class APermissionIsDeclaredOnTheAssemblyTests
{
    private static string Model(string declarations, string operations = "") => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authorization;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;

        {{declarations}}

        namespace TestApp
        {
            [Boundary]
            public partial class PeopleBoundary { }

            [Entity]
            [BelongsTo<PeopleBoundary>]
            public partial class Team : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }

            [PragmaticDbContext("People")]
            public partial class PeopleDbContext { }

            {{operations}}
        }
        """;

    private const string Declared = """
        [assembly: Permission("people.personal-data.erase", "Erase a person's data", Category = "Privacy")]
        [assembly: Permission("people.team.archive", "Archive a team")]
        """;

    private const string UsesTheConstant = """
        [DomainAction]
        [RequirePermission(PeoplePermissions.PersonalData.Erase)]
        public partial class EraseAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    [Fact]
    public void TheConstant_IsAConstOfTheBoundarysPermissionsClass()
    {
        var permissions = PermissionsClass(Model(Declared));

        permissions.Should().Contain("public static partial class PersonalData");
        permissions.Should().Contain("public const string Erase = \"people.personal-data.erase\";");
        permissions.Should().Contain("public const string Archive = \"people.team.archive\";",
            "a resource that is an entity takes the constant into that entity's class");
    }

    /// <summary>One class, with a described <c>[RequirePermission]</c> beside the attributes.</summary>
    [Fact]
    public void ThereIsOnePeoplePermissionsClass()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model(Declared, """
            [DomainAction]
            [RequirePermission("people.audit.verify", Description = "Verify the audit trail")]
            public partial class VerifyAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """));

        sources.Values.Sum(s => Regex.Matches(s, @"\bclass PeoplePermissions\b").Count).Should().Be(1);
    }

    [Fact]
    public void TheRegistry_ListsItWithItsDescriptionAndCategory()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model(Declared));
        var registry = sources.First(s => s.Key.Contains("PermissionRegistry")).Value;

        registry.Should().Contain("new PermissionInfo(\"people.personal-data.erase\", \"Erase a person's data\", \"Privacy\")");
        registry.Should().Contain("new PermissionInfo(\"people.team.archive\", \"Archive a team\", null)");
    }

    /// <summary>The constant compiles in an attribute, and the requirement names its value — not fail-open.</summary>
    [Fact]
    public void ARequirementNamingTheConstant_Compiles_AndIsEnforced()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model(Declared, UsesTheConstant),
            static path => path.Contains("EraseAction") || path.Contains("Permission") || path.EndsWith("TestSource.cs"));
        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));

        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model(Declared, UsesTheConstant));
        diagnostics.Should().NotContain(d => d.Id == "PRAG0418", "the constant resolves to its value");
        sources.First(s => s.Key.Contains("PermissionRequirementRegistry")).Value
            .Should().Contain("\"people.personal-data.erase\"");
    }

    [Fact]
    public void AValueOutsideTheBoundarysPrefix_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model(
            "[assembly: Permission(\"billing.invoice.refund\", \"Refund an invoice\")]"));

        diagnostics.Where(d => d.Id == "PRAG1004").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("billing.invoice.refund") && m.Contains("people"));
    }

    /// <summary>An empty value is reported, not dropped: the attribute compiles, so silence would be a line that does nothing.</summary>
    [Theory]
    [InlineData("[assembly: Permission(\"\", \"Nothing\")]")]
    [InlineData("[assembly: Permission(\" \", \"Nothing\")]")]
    public void AnEmptyValue_IsReported(string declarations)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model(declarations));

        diagnostics.Should().Contain(d => d.Id == "PRAG1004");
    }

    [Theory]
    [InlineData("[assembly: Permission(\"people.team.archive\", \"A\")]\n[assembly: Permission(\"people.team.archive\", \"B\")]")]
    [InlineData("[assembly: Permission(\"people.team.read\", \"Read a team\")]")]
    public void AValueDeclaredTwice_IsReported(string declarations)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model(declarations));

        diagnostics.Where(d => d.Id == "PRAG1001").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("people.team."));
    }

    /// <summary>
    ///     A value whose constant would share its name with a class of the same file — the entity's, another
    ///     declaration's, the enclosing one — is reported, not rendered into a CS0102 the author cannot open.
    /// </summary>
    [Theory]
    [InlineData("[assembly: Permission(\"people.team\", \"Anything on teams\")]", "PeoplePermissions.Team")]
    [InlineData("[assembly: Permission(\"people.personal-data\", \"A\")]\n[assembly: Permission(\"people.personal-data.erase\", \"B\")]", "PeoplePermissions.PersonalData")]
    [InlineData("[assembly: Permission(\"people.team.team\", \"A\")]", "PeoplePermissions.Team.Team")]
    public void AValueWhoseConstantNamesAClass_IsReported_AndTheClassCompiles(string declarations, string name)
    {
        var source = Model(declarations);
        var (_, diagnostics) = TraitCompilationHarness.Generate(source);

        diagnostics.Where(d => d.Id == "PRAG1005").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains(name));

        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(source, static path => path.Contains("Permission"));
        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>
    ///     <c>[RequirePermission("…", Description = "…")]</c> takes the same path: the constant in the one class,
    ///     the entry in the registry.
    /// </summary>
    [Fact]
    public void ARequirementWithADescription_IsDeclaredTheSameWay()
    {
        var source = Model("", """
            [DomainAction]
            [RequirePermission("people.team.transfer", Description = "Transfer a team", Category = "Teams")]
            public partial class TransferAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """);
        var (sources, _) = TraitCompilationHarness.Generate(source);

        PermissionsClass(source).Should().Contain("public const string Transfer = \"people.team.transfer\";");
        sources.First(s => s.Key.Contains("PermissionRegistry")).Value
            .Should().Contain("new PermissionInfo(\"people.team.transfer\", \"Transfer a team\", \"Teams\")");
        sources.Keys.Should().NotContain(k => k.Contains("CustomPermissions"));
    }

    /// <summary>
    ///     An assembly with no boundary — a package like Authorization.Management — has no boundary class for
    ///     the constant to go into: its first segment names the class, and a first segment that is an entity's
    ///     own CRUD class takes the constant into it.
    /// </summary>
    private const string NoBoundary = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authorization;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        [assembly: Permission("authorization.roles.manage", "Manage roles", Category = "Authorization")]
        [assembly: Permission("authorization.view", "View the configuration")]
        [assembly: Permission("team.archive", "Archive a team")]

        namespace Admin.Rbac.Entities
        {
            [Entity]
            public partial class Team : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }
        }

        namespace Admin.Rbac.Actions
        {
            [DomainAction]
            [RequirePermission(AuthorizationPermissions.Roles.Manage, AuthorizationPermissions.View, TeamPermissions.Archive)]
            public partial class ManageAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
        }
        """;

    [Fact]
    public void InAnAssemblyWithNoBoundary_TheFirstSegmentNamesTheClass()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(NoBoundary);

        diagnostics.Should().NotContain(d => d.Id == "PRAG1004" || d.Id == "PRAG1005" || d.Id == "PRAG0418");
        sources.First(s => s.Key.Contains("PermissionRegistry")).Value
            .Should().Contain("new PermissionInfo(\"authorization.roles.manage\", \"Manage roles\", \"Authorization\")");
        sources.First(s => s.Key.Contains("PermissionRequirementRegistry")).Value
            .Should().Contain("\"authorization.roles.manage\"").And.Contain("\"team.archive\"");

        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(NoBoundary,
            static path => path.Contains("Permission") || path.Contains("ManageAction") || path.EndsWith("TestSource.cs"));
        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string PermissionsClass(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var match = sources.FirstOrDefault(s => s.Key.Contains("EntityPermissions"));
        match.Value.Should().NotBeNull($"the permissions class is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
