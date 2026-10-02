using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[LoadEntity&lt;T&gt;]</c> on a mutation gets what it gets on an action: the field, the repository
///     that fills it, and an invoker that loads it — after authorization, 404 for a key that names nothing.
/// </summary>
/// <remarks>
///     The attribute targets any class, so it compiled on a <c>Mutation&lt;TEntity&gt;</c> too, and nothing
///     read it there: no field, no load, no diagnostic. Time off's <c>CreateTeamMutation</c>
///     injected <c>IRepository&lt;Employee&gt;</c> by hand to load the manager for exactly this.
/// </remarks>
public class LoadEntityOnAMutationTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;

        namespace TestApp.Teams;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
            public Guid ManagerId { get; set; }
        }

        [Mutation(Mode = MutationMode.Create)]
        [LoadEntity<Employee>(nameof(ManagerId))]
        public partial class CreateTeamMutation : Mutation<Team>
        {
            public required string Name { get; init; }
            public required Guid ManagerId { get; init; }
        }
        """;

    [Fact]
    public void TheMutation_GetsTheFieldAndItsRepository()
    {
        var generated = GetGeneratedSource(RunGeneratorWithEntities(Source), "CreateTeamMutation.LoadEntity");

        generated.Should().NotBeNull("[LoadEntity] promises a field, and the partial that declares it");
        generated!.Should().Contain("_employee")
            .And.Contain("_employeeRepository")
            .And.Contain("SetLoadedEntities");
    }

    [Fact]
    public void TheRepository_IsInjectedIntoTheMutation()
    {
        var generated = GetGeneratedSource(RunGeneratorWithEntities(Source), "CreateTeamMutation.SetDependencies");

        generated.Should().NotBeNull();
        generated!.Should().Contain("_employeeRepository = employeeRepository;");
    }

    [Fact]
    public void TheInvoker_LoadsItBeforeTheMutationRuns_AndAnswers404WhenItIsMissing()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "CreateTeamMutation.MutationInvoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("PrepareMutationAsync(")
            .And.Contain("GetByIdAsync(mutation.ManagerId, ct)")
            .And.Contain("NotFoundError.For(\"Employee\", mutation.ManagerId.ToString())")
            .And.Contain("mutation.SetLoadedEntities(employee);");
    }

    [Fact]
    public void TheGeneratedMutation_Compiles()
    {
        var result = RunGeneratorWithEntities(Source);

        GetCompilationErrors(result)
            .Where(e => e.Location.SourceTree?.FilePath.Contains("CreateTeamMutation") == true)
            .Select(e => e.ToString())
            .Should().BeEmpty();
    }

    /// <summary>A key the mutation does not carry is reported, as on an action.</summary>
    [Fact]
    public void AKeyTheMutationDoesNotCarry_IsReported()
    {
        var source = Source.Replace("[LoadEntity<Employee>(nameof(ManagerId))]", "[LoadEntity<Employee>(\"LeaderId\")]");

        HasDiagnostic(RunGeneratorWithEntities(source), "PRAG0404").Should().BeTrue();
    }

    /// <summary>The control: a mutation that declares nothing gets no preparation.</summary>
    [Fact]
    public void AMutationThatDeclaresNothing_GetsNoPreparation()
    {
        var source = Source.Replace("[LoadEntity<Employee>(nameof(ManagerId))]", "");

        GetGeneratedSource(RunGeneratorWithEntities(source), "CreateTeamMutation.MutationInvoker")!
            .Should().NotContain("PrepareMutationAsync");
    }
}
