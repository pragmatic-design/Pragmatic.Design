using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[LoadEntity&lt;T&gt;(nameof(Property), By = nameof(T.Key))]</c>: the row read by the entity's logic key,
///     through the lookup the generator writes for it, instead of by id.
/// </summary>
/// <remarks>
///     <para>
///         A load was by primary key only, so an operation that names a row by its domain key — a number,
///         a code, a slug — injected the repository, called <c>GetBy{Key}Async</c> and answered the 404 by hand,
///         although the generator already wrote the lookup.
///     </para>
///     <para>
///         The lookup is written by the persistence generator, which runs when EF Core is present — stubbed here
///         the way <c>AKeyReturnTypeLoadsNoResponseNavigationsTests</c> stubs it. The rest of the EF output does not
///         compile against a stub, so what is checked is the invoker: the call it makes binds to the generated
///         member.
///     </para>
/// </remarks>
public class AnOperationLoadsByTheLogicKeyTests : ActionsGeneratorTestBase
{
    private const string Entities = """
        [Entity]
        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }

            [LogicKey]
            public string Number { get; set; } = "";

            public string Name { get; set; } = "";

            public Team? Team { get; set; }
        }

        [Entity]
        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }

            public string Name { get; set; } = "";
        }
        """;

    private static string Source(string operations) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }

        namespace TestApp.People
        {
            {{Entities}}

            {{operations}}
        }
        """;

    /// <param name="attribute">The load, as written on the action.</param>
    /// <param name="property">The action's property the load reads its key from.</param>
    private static string Action(string attribute, string property = "public required string EmployeeNumber { get; init; }")
        => Source($$"""
            [DomainAction]
            {{attribute}}
            public partial class FindEmployeeAction : DomainAction<Guid>
            {
                {{property}}

                public Guid EmployeeId { get; init; }

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<Guid, IError>>(_employee.PersistenceId);
            }
            """);

    /// <summary>The invoker's source, after checking that nothing in it — nor in the action — fails to bind.</summary>
    private static string Invoker(SourceGenRunResult result)
    {
        var errors = GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath is { } path
                        && (path.Contains("FindEmployeeAction") || path.Contains("TestSource")))
            .Select(d => d.ToString())
            .ToList();
        errors.Should().BeEmpty(string.Join(" | ", errors));

        return GetGeneratedSource(result, "FindEmployeeAction.Invoker")!;
    }

    [Fact]
    public void ARowNamedByItsLogicKey_IsReadThroughTheGeneratedLookup_And404NamesTheKey()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action(
            "[LoadEntity<Employee>(nameof(EmployeeNumber), By = nameof(Employee.Number))]")));

        invoker.Should().Contain(
            "global::TestApp.People.EmployeeSpecifications.GetByNumberAsync(_employeeRepository, action.EmployeeNumber, ct)");
        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.For(\"Employee\", action.EmployeeNumber)");
        invoker.Should().NotContain("GetByIdAsync", "the key is the domain key, not the primary key");
    }

    /// <summary>With <c>Include</c> the read is the filtered query and the generated specification.</summary>
    [Fact]
    public void WithAnInclude_TheSpecificationIsReadThroughTheQuery()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action(
            "[LoadEntity<Employee>(nameof(EmployeeNumber), By = nameof(Employee.Number), Include = \"Team\")]")));

        invoker.Should().Contain("\"Team\"");
        invoker.Should().Contain("global::TestApp.People.EmployeeSpecifications.ByNumber(action.EmployeeNumber)");
    }

    /// <summary>The control: without <c>By</c> the key is the primary key, read as before.</summary>
    [Fact]
    public void WithoutBy_TheKeyIsTheId()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action("[LoadEntity<Employee>(nameof(EmployeeId))]")));

        invoker.Should().Contain("_employeeRepository.GetByIdAsync(action.EmployeeId, ct)");
        invoker.Should().NotContain("GetByNumberAsync");
    }

    [Fact]
    public void By_NamingAMemberThatIsNotTheLogicKey_IsReported()
    {
        var result = RunGeneratorWithEntities(Action(
            "[LoadEntity<Employee>(nameof(EmployeeNumber), By = nameof(Employee.Name))]"));

        var reported = GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0460").ToList();
        reported.Should().ContainSingle();
        reported[0].GetMessage().Should().Contain("'Name'").And.Contain("'Number'");
    }

    [Fact]
    public void By_OnAnEntityWithoutALogicKey_IsReported()
    {
        var result = RunGeneratorWithEntities(Source("""
            [DomainAction]
            [LoadEntity<Team>(nameof(TeamName), By = nameof(Team.Name))]
            public partial class FindTeamAction : DomainAction<Guid>
            {
                public required string TeamName { get; init; }

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<Guid, IError>>(_team.PersistenceId);
            }
            """));

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0460").Select(d => d.GetMessage())
            .Should().ContainSingle(m => m.Contains("declares no [LogicKey]"));
    }

    [Fact]
    public void By_BesideASpecification_IsReported()
    {
        var result = RunGeneratorWithEntities(Action(
            "[LoadEntity<Employee>(Specification = \"ById\", By = nameof(Employee.Number))]"));

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0460").Select(d => d.GetMessage())
            .Should().ContainSingle(m => m.Contains("Specification"));
    }

    [Fact]
    public void By_WithAPropertyOfAnotherType_IsReported()
    {
        var result = RunGeneratorWithEntities(Action(
            "[LoadEntity<Employee>(nameof(EmployeeNumber), By = nameof(Employee.Number))]",
            property: "public int EmployeeNumber { get; init; }"));

        var reported = GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0461").ToList();
        reported.Should().ContainSingle();
        reported[0].GetMessage().Should().Contain("'int'").And.Contain("'string'");
    }
}
