using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[LoadEntity(Specification = …)]</c> and <c>[LoadEntities(Specification = …)]</c>: the rows an
///     operation needs, read by a named rule whose parameters bind by name to the operation's properties.
/// </summary>
/// <remarks>
///     A load was by key only, so "the active employee with this number" or "the pending requests
///     of this employee" was a repository injected by hand, a <c>FirstOrDefaultAsync(spec)</c> and a null
///     check — Time off's transfer read the requests it moves that way.
/// </remarks>
public class AnOperationLoadsByANamedRuleTests : ActionsGeneratorTestBase
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        using Pragmatic.Specification;

        namespace TestApp.People;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Number { get; set; } = "";
            public bool IsActive { get; set; }
            public Guid TeamId { get; set; }
        }

        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
        }

        public static class EmployeeSpecifications
        {
            public static Specification<Employee> Active => Spec<Employee>.Where(e => e.IsActive);

            public static Specification<Employee> ActiveWithNumber(string number)
                => Spec<Employee>.Where(e => e.IsActive && e.Number == number);

            public static Specification<Employee> InTeam(Guid teamId, bool activeOnly = true)
                => Spec<Employee>.Where(e => e.TeamId == teamId && (!activeOnly || e.IsActive));

            public static string Describe(string number) => number;
        }

        """;

    private const string Loads = Header + """
        [DomainAction]
        [LoadEntity<Employee>(Specification = nameof(EmployeeSpecifications.ActiveWithNumber))]
        public partial class FindByNumberAction : DomainAction<Guid>
        {
            public required string Number { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<Guid, IError>>(_employee.PersistenceId);
        }

        [DomainAction]
        [LoadEntities<Employee>(Specification = nameof(EmployeeSpecifications.InTeam))]
        public partial class CountTeamAction : DomainAction<int>
        {
            public required Guid TeamId { get; init; }

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<int, IError>>(_employees.Count);
        }

        [DomainAction]
        [LoadEntities<Employee>(Specification = "Active", RequireAny = true)]
        public partial class CountActiveAction : DomainAction<int>
        {
            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<int, IError>>(_employees.Count);
        }

        [Mutation(Mode = MutationMode.Update)]
        [LoadEntities<Employee>(Specification = nameof(EmployeeSpecifications.InTeam), FieldName = "_members")]
        public partial class RenameTeamMutation : Mutation<Team>
        {
            public required Guid Id { get; init; }

            public string Name { get; init; } = "";

            public Guid TeamId { get; init; }
        }
        """;

    [Fact]
    public void ARow_IsReadByTheRule_ItsParameterBoundToTheProperty()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Loads), "FindByNumberAction.Invoker")!;

        invoker.Should().Contain(
            "_employeeRepository.FirstOrDefaultAsync(global::TestApp.People.EmployeeSpecifications.ActiveWithNumber(number: action.Number), ct)");
        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.For(\"Employee\")",
            "a rule that matches nothing is a 404, as a key that names nothing is");
    }

    [Fact]
    public void Rows_AreReadByTheRule_AndNoneIsAnEmptyList()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Loads), "CountTeamAction.Invoker")!;

        invoker.Should().Contain(
            "_employeesRepository.FindAsync(global::TestApp.People.EmployeeSpecifications.InTeam(teamId: action.TeamId), ct)",
            "an optional parameter with no property of its name keeps its default");
        invoker.Should().NotContain("NotFoundError", "no row is an empty list unless RequireAny says otherwise");
    }

    [Fact]
    public void RequireAny_AnswersNoRowsWith404_AndAPlainNameIsAMemberOfTheEntitysSpecifications()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Loads), "CountActiveAction.Invoker")!;

        invoker.Should().Contain("_employeesRepository.FindAsync(global::TestApp.People.EmployeeSpecifications.Active, ct)");
        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.For(\"Employee\")");
    }

    /// <summary>The control: the key form is read as before, by its key.</summary>
    [Fact]
    public void TheKeyForm_IsReadByItsKey()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntity<Employee>(nameof(EmployeeId))]
            public partial class LookAction : DomainAction<bool>
            {
                public required Guid EmployeeId { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        var invoker = GetGeneratedSource(result, "LookAction.Invoker")!;
        invoker.Should().Contain("_employeeRepository.GetByIdAsync(action.EmployeeId, ct)");
        invoker.Should().NotContain("FirstOrDefaultAsync(");
    }

    [Fact]
    public void TheRuleLoads_Compile_AndTheRulesInputsAreNoInputToMap()
    {
        var result = RunGeneratorWithEntities(Loads);

        // Every PRAG diagnostic: the mutation's TeamId feeds the rule and Team has no member of that name,
        // so a mapping warning for it would fail a module built with warnings as errors.
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG")
            .Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => d.ToString()).Should().BeEmpty();
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();
        GetGeneratedSource(result, "RenameTeamMutation.MutationInvoker")!
            .Should().Contain("_membersRepository.FindAsync(global::TestApp.People.EmployeeSpecifications.InTeam(teamId: mutation.TeamId), ct)");
    }

    [Fact]
    public void AParameterThatBindsNoProperty_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntity<Employee>(Specification = nameof(EmployeeSpecifications.ActiveWithNumber))]
            public partial class LookAction : DomainAction<bool>
            {
                public required string Code { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0455").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("LookAction") && m.Contains("'number'"));
        GetCompilationErrors(result).Where(e => e.Location.SourceTree?.FilePath.Contains(".g.cs") == true)
            .Select(e => e.ToString()).Should().BeEmpty("the operation is reported, not a file the author cannot open");
    }

    [Fact]
    public void AParameterOfAnotherType_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntities<Employee>(Specification = nameof(EmployeeSpecifications.InTeam))]
            public partial class LookAction : DomainAction<bool>
            {
                public required string TeamId { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0455").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("'teamId'") && m.Contains("string"));
    }

    [Theory]
    [InlineData("nameof(EmployeeSpecifications.Describe)")]
    [InlineData("\"Nowhere\"")]
    public void AMemberThatIsNoSpecificationOfTheEntity_IsReported(string specification)
    {
        var result = RunGeneratorWithEntities(Header + $$"""
            [DomainAction]
            [LoadEntity<Employee>(Specification = {{specification}})]
            public partial class LookAction : DomainAction<bool>
            {
                public required string Number { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0454").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("LookAction"));
    }

    [Theory]
    [InlineData("nameof(EmployeeId), Specification = nameof(EmployeeSpecifications.Active)")]
    [InlineData("")]
    public void AKeyAndARule_OrNeither_IsReported(string arguments)
    {
        var result = RunGeneratorWithEntities(Header + $$"""
            [DomainAction]
            [LoadEntity<Employee>({{arguments}})]
            public partial class LookAction : DomainAction<bool>
            {
                public required Guid EmployeeId { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0456").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("LookAction"));
    }

    /// <summary>
    ///     An entity the permission catalogue does not know — here, one the persistence generator does not
    ///     generate — has no read permission to ask: a build error, not a check that silently asks nothing.
    /// </summary>
    [Fact]
    public void ReadPermissionOfAnEntityWithNone_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntities<Employee>(Specification = nameof(EmployeeSpecifications.Active), RequireReadPermission = true)]
            public partial class LookAction : DomainAction<bool>
            {
                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0457").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("LookAction") && m.Contains("Employee"));
    }
}
