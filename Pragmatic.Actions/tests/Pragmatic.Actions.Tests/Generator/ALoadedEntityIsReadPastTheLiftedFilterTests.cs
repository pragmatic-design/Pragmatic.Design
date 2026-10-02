using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An operation that lifts a filter with <c>[WithoutFilter&lt;T&gt;]</c> preloads its
///     <c>[LoadEntity]</c> entities past that filter, as its body reads.
/// </summary>
/// <remarks>
///     The invoker opened the filter scopes around the execution only, and the preload runs
///     before it. An action declared to work on a soft-deleted employee answered 404 for that employee
///     when it loaded them with <c>[LoadEntity]</c> — which is why Time off's erasure loaded them by hand.
/// </remarks>
public class ALoadedEntityIsReadPastTheLiftedFilterTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Filters;
        using Pragmatic.Result;

        namespace TestApp.People;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        public partial class Note : IEntity
        {
            public Guid PersistenceId { get; set; }
            public Guid EmployeeId { get; set; }
        }

        [DomainAction]
        [WithoutFilter<Employee>]
        [LoadEntity<Employee>(nameof(Id))]
        public partial class EraseAction : DomainAction<bool>
        {
            public required Guid Id { get; init; }

            public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<bool, IError>>(true);
        }

        [Mutation(Mode = MutationMode.Create)]
        [WithoutFilter<Employee>]
        [LoadEntity<Employee>(nameof(EmployeeId))]
        public partial class WriteNoteMutation : Mutation<Note>
        {
            public required Guid EmployeeId { get; init; }
        }

        [DomainAction]
        [LoadEntity<Employee>(nameof(Id))]
        public partial class LookAction : DomainAction<bool>
        {
            public required Guid Id { get; init; }

            public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<bool, IError>>(true);
        }
        """;

    [Fact]
    public void AnAction_PreloadsInsideTheLiftedFilter()
        => Preparation("EraseAction.Invoker", "PrepareActionAsync(")
            .Should().Contain("Disable(typeof(global::TestApp.People.Employee))");

    [Fact]
    public void AMutation_PreloadsInsideTheLiftedFilter()
        => Preparation("WriteNoteMutation.MutationInvoker", "PrepareMutationAsync(")
            .Should().Contain("Disable(typeof(global::TestApp.People.Employee))");

    /// <summary>The control: an operation that lifts nothing preloads with the filters on.</summary>
    [Fact]
    public void AnOperationThatLiftsNothing_PreloadsAsBefore()
        => Preparation("LookAction.Invoker", "PrepareActionAsync(")
            .Should().NotContain("Disable(");

    [Fact]
    public void TheGeneratedOperations_Compile()
        => GetCompilationErrors(RunGeneratorWithEntities(Source))
            .Where(e => e.Location.SourceTree?.FilePath.Contains("Action") == true
                        || e.Location.SourceTree?.FilePath.Contains("Mutation") == true)
            .Select(e => e.ToString())
            .Should().BeEmpty();

    /// <summary>The body of the invoker's preparation method, up to the hand-over of the loaded entities.</summary>
    private static string Preparation(string hint, string method)
    {
        var result = RunGeneratorWithEntities(Source);
        var invoker = GetGeneratedSource(result, hint);
        invoker.Should().NotBeNull(
            $"{hint} is generated at all; generated: {string.Join(", ", GetGeneratedSourcesAsDictionary(result).Keys)}");

        var start = invoker!.IndexOf(method, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{hint} prepares the [LoadEntity] entities");
        var end = invoker.IndexOf("SetLoadedEntities(", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return invoker[start..end];
    }
}
