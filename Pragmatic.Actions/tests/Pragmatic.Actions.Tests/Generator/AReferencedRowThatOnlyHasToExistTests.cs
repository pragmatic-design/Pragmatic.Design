using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[RequireExists&lt;T&gt;(nameof(Key))]</c>: a referenced row the operation only needs to exist, checked with
///     <c>ExistsAsync</c> before the body — 404 when absent — without reading it.
/// </summary>
/// <remarks>
///     Such a reference — a foreign key in the body of a create — was either loaded whole with
///     <c>[LoadEntity]</c>, a row materialized for nothing, or left to the database, whose foreign-key violation
///     answered instead of a 404 naming the row.
/// </remarks>
public class AReferencedRowThatOnlyHasToExistTests : ActionsGeneratorTestBase
{
    private const string Header = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.People;

        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
            public Guid TeamId { get; set; }
        }

        """;

    private static string Action(string attributes, string key = "public required Guid TeamId { get; init; }") => Header + $$"""
        [DomainAction]
        {{attributes}}
        public partial class JoinTeamAction : DomainAction<bool>
        {
            {{key}}

            public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<bool, IError>>(true);
        }
        """;

    private static void NothingFailsToBind(Pragmatic.SourceGen.Testing.SourceGenRunResult result)
    {
        var errors = GetCompilationErrors(result).Select(d => d.ToString()).ToList();
        errors.Should().BeEmpty(string.Join(" | ", errors));
    }

    [Fact]
    public void AReferencedRow_IsCheckedWithExists_AndNotLoaded()
    {
        var result = RunGeneratorWithEntities(Action("[RequireExists<Team>(nameof(TeamId))]"));

        NothingFailsToBind(result);
        var invoker = GetGeneratedSource(result, "JoinTeamAction.Invoker")!;
        invoker.Should().Contain("_teamRepository.ExistsAsync(");
        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.For(\"Team\", action.TeamId.ToString())");
        invoker.Should().NotContain("GetByIdAsync", "the row only has to exist: nothing is read");

        var partial = GetGeneratedSource(result, "JoinTeamAction.LoadEntity")!;
        partial.Should().Contain("_teamRepository");
        partial.Should().NotContain("global::TestApp.People.Team _team", "there is no loaded row to hold");
    }

    [Fact]
    public void ANullKey_IsNotChecked()
    {
        var result = RunGeneratorWithEntities(Action(
            "[RequireExists<Team>(nameof(TeamId))]", key: "public Guid? TeamId { get; init; }"));

        NothingFailsToBind(result);
        GetGeneratedSource(result, "JoinTeamAction.Invoker")!
            .Should().Contain("if (action.TeamId is { } __teamIdKey && !await _teamRepository.ExistsAsync(");
    }

    [Fact]
    public void OnAMutation_TheReferenceIsCheckedAndStillWritten()
    {
        var result = RunGeneratorWithEntities(Header + """
            [Mutation(Mode = MutationMode.Create)]
            [RequireExists<Team>(nameof(TeamId))]
            public partial class HireEmployeeMutation : Mutation<Employee>
            {
                public required string Name { get; init; }
                public required Guid TeamId { get; init; }
            }
            """);

        NothingFailsToBind(result);
        GetGeneratedSource(result, "HireEmployeeMutation.MutationInvoker")!.Should().Contain("_teamRepository.ExistsAsync(");
        GetGeneratedSource(result, "HireEmployeeMutation.ApplyToEntity")!.Should().Contain("TeamId",
            "the key is a member of the entity, so it is written as any other input");
    }

    [Fact]
    public void AKeyOfAnotherType_IsReported()
    {
        var result = RunGeneratorWithEntities(Action(
            "[RequireExists<Team>(nameof(TeamId))]", key: "public required string TeamId { get; init; }"));

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0411").Select(d => d.GetMessage())
            .Should().ContainSingle(m => m.Contains("RequireExists"));
    }

    [Fact]
    public void BesideALoadOfTheSameKey_ItIsReported_AndTheLoadAloneRuns()
    {
        var result = RunGeneratorWithEntities(Action(
            "[RequireExists<Team>(nameof(TeamId))]\n[LoadEntity<Team>(nameof(TeamId))]"));

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG0462").Should().ContainSingle(
            "the load already proves the row exists");
        var invoker = GetGeneratedSource(result, "JoinTeamAction.Invoker")!;
        invoker.Should().Contain("GetByIdAsync(action.TeamId, ct)");
        invoker.Should().NotContain("ExistsAsync");
    }

    /// <summary>The control: a load reads the row, and asks no existence of it.</summary>
    [Fact]
    public void ALoadAlone_ReadsTheRow()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Action("[LoadEntity<Team>(nameof(TeamId))]")),
            "JoinTeamAction.Invoker")!;

        invoker.Should().Contain("_teamRepository.GetByIdAsync(action.TeamId, ct)");
        invoker.Should().NotContain("ExistsAsync");
    }
}
