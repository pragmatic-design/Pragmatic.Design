using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[LoadEntities&lt;T&gt;(nameof(Ids))]</c>: the rows named by a list of keys the operation carries,
///     read in one query, and one 404 naming every key that names nothing.
/// </summary>
/// <remarks>
///     The list counterpart of the single form. Without it, an operation that needs several rows writes
///     the read by hand — a <c>FindAsync(Spec.Where(x =&gt; ids.Contains(x.Id)))</c> and a comparison to
///     find the missing ones, which is where a key that names nothing gets forgotten.
/// </remarks>
public class AListOfKeysLoadsItsRowsTests : ActionsGeneratorTestBase
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

        namespace TestApp.People;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
        }

        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
        }

        """;

    private const string Loads = Header + """
        [DomainAction]
        [LoadEntities<Employee>(nameof(EmployeeIds))]
        public partial class NameEmployeesAction : DomainAction<int>
        {
            public required IReadOnlyList<Guid> EmployeeIds { get; init; }

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<int, IError>>(_employees.Count);
        }

        [DomainAction]
        [LoadEntity<Team>(nameof(TeamId))]
        [LoadEntities<Employee>(nameof(ReviewerIds), FieldName = "_reviewers")]
        public partial class ReviewTeamAction : DomainAction<string>
        {
            public required Guid TeamId { get; init; }

            public Guid[] ReviewerIds { get; init; } = [];

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<string, IError>>(_team.Name + _reviewers.Count);
        }

        [Mutation(Mode = MutationMode.Update)]
        [LoadEntities<Employee>(nameof(MemberIds))]
        public partial class RenameTeamMutation : Mutation<Team>
        {
            public required Guid Id { get; init; }

            public string Name { get; init; } = "";

            public List<Guid> MemberIds { get; init; } = [];
        }
        """;

    [Fact]
    public void AListOfKeys_IsReadInOneQuery()
    {
        var result = RunGeneratorWithEntities(Loads);

        GetGeneratedSource(result, "NameEmployeesAction.LoadEntity")!
            .Should().Contain("global::System.Collections.Generic.IReadOnlyList<global::TestApp.People.Employee> _employees");

        var invoker = GetGeneratedSource(result, "NameEmployeesAction.Invoker")!;
        invoker.Should().Contain("_employeesRepository.FindAsync(");
        invoker.Should().Contain("global::Pragmatic.Specification.Spec<global::TestApp.People.Employee>.Where(");
        invoker.Should().Contain("global::System.Linq.Enumerable.Distinct(",
            "a key given twice is one row, read once");
        invoker.Should().NotContain("GetByIdAsync(", "one query for the list, not one per key");
    }

    [Fact]
    public void TheMissingKeys_AreOne404NamingThemAll()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Loads), "NameEmployeesAction.Invoker")!;

        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.ForAll(\"Employee\", ");
    }

    /// <summary>The control: a single load beside the list is read as before, by its key.</summary>
    [Fact]
    public void ASingleLoadBesideIt_IsReadByItsKey()
    {
        var result = RunGeneratorWithEntities(Loads);

        var invoker = GetGeneratedSource(result, "ReviewTeamAction.Invoker")!;
        invoker.Should().Contain("_teamRepository.GetByIdAsync(action.TeamId, ct)");
        invoker.Should().Contain("_reviewersRepository.FindAsync(");
        GetGeneratedSource(result, "ReviewTeamAction.LoadEntity")!
            .Should().Contain("IReadOnlyList<global::TestApp.People.Employee> _reviewers");
    }

    [Fact]
    public void TheListLoads_Compile()
    {
        var result = RunGeneratorWithEntities(Loads);

        // Every PRAG diagnostic, not only the actions': the key list of a mutation has no member on the
        // entity, and a mapping warning for it would fail a module built with warnings as errors.
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG")
            .Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => d.ToString()).Should().BeEmpty();
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();
        GetGeneratedSource(result, "RenameTeamMutation.MutationInvoker")!
            .Should().Contain("_employeesRepository.FindAsync(", "a mutation loads the list the way an action does");
    }

    /// <summary>
    ///     The key of a load is no input to map when the entity has no member of its name — the list's
    ///     above, and the single form's here, which was the same warning before the list existed.
    /// </summary>
    [Fact]
    public void ASingleKeyTheEntityDoesNotHave_IsNoInputToMap()
    {
        var result = RunGeneratorWithEntities(Header + """
            [Mutation(Mode = MutationMode.Update)]
            [LoadEntity<Employee>(nameof(ReviewerId))]
            public partial class ReviewTeamMutation : Mutation<Team>
            {
                public required Guid Id { get; init; }

                public required Guid ReviewerId { get; init; }
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG")
            .Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => d.ToString()).Should().BeEmpty();
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("IReadOnlyList<string>")]
    [InlineData("Guid")]
    [InlineData("List<Guid?>")]
    public void KeysThatAreNotAListOfTheEntitysKey_AreReported(string type)
    {
        var result = RunGeneratorWithEntities(Header + $$"""
            [DomainAction]
            [LoadEntities<Employee>(nameof(Keys))]
            public partial class LookAction : DomainAction<bool>
            {
                public required {{type}} Keys { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0411").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("[LoadEntities<Employee>]") && m.Contains("LookAction"));
        GetCompilationErrors(result).Where(e => e.Location.SourceTree?.FilePath.Contains(".g.cs") == true)
            .Select(e => e.ToString()).Should().BeEmpty("the operation is reported, not a file the author cannot open");
    }

    [Fact]
    public void APropertyThatIsNotThere_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntities<Employee>("Missing")]
            public partial class LookAction : DomainAction<bool>
            {
                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0404").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("[LoadEntities<Employee>]") && m.Contains("'Missing'"));
    }
}
