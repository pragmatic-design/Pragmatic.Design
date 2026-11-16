using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A dependency field declared nullable is optional (#812): the invoker takes it as <c>T? name = null</c>, after the required
///     parameters, so a host that registers nothing for it constructs the invoker and the operation reads null.
/// </summary>
/// <remarks>
///     Before #812 the field's type reached the invoker without its annotation, as a required parameter, so the container refused to build
///     the invoker in a host without the service, and the operation's "null when absent" branch never ran. The effect is measured in a
///     container by <c>Pragmatic.Authorization.Management.Tests</c> (<c>AnOptionalDependencyIsNotRequiredTests</c>); this pins the shape.
/// </remarks>
public class ANullableDependencyIsOptionalTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Sales;

        public interface INoteArchive { void Keep(string text); }
        public interface INoteClock { DateTimeOffset Now { get; } }

        public partial class Note : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [Mutation(Mode = MutationMode.Create)]
        public partial class CreateNoteMutation : Mutation<Note>
        {
            private INoteArchive? _archive;

            public required string Text { get; init; }
        }

        [DomainAction]
        public partial class WriteNoteAction : VoidDomainAction
        {
            private INoteClock _clock = null!;
            private INoteArchive? _archive;

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            {
                _archive?.Keep(_clock.Now.ToString());
                return Task.FromResult(VoidResult<IError>.Success());
            }
        }
        """;

    [Fact]
    public void AnActionsNullableField_IsAnOptionalParameter_AfterTheRequiredOnes()
    {
        var result = RunGeneratorWithEntities(Source);
        var invoker = GetGeneratedSource(result, "WriteNoteAction.Invoker")!;

        invoker.Should().Contain("global::TestApp.Sales.INoteArchive? archive = null");
        invoker.IndexOf("INoteArchive? archive = null", StringComparison.Ordinal)
            .Should().BeGreaterThan(invoker.IndexOf("serviceProvider", StringComparison.Ordinal),
                "an optional parameter follows every required one");
        HasCompilationErrors(result).Should().BeFalse();
    }

    /// <summary>The control: a field declared non-nullable stays required.</summary>
    [Fact]
    public void AnActionsNonNullableField_StaysRequired()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "WriteNoteAction.Invoker")!;

        invoker.Should().Contain("global::TestApp.Sales.INoteClock clock,");
        invoker.Should().NotContain("INoteClock? clock");
    }

    [Fact]
    public void AMutationsNullableField_IsAnOptionalParameter()
    {
        var result = RunGeneratorWithEntities(Source);
        var invoker = GetGeneratedSource(result, "CreateNoteMutation.MutationInvoker")!;

        invoker.Should().Contain("global::TestApp.Sales.INoteArchive? archive = null");
        HasCompilationErrors(result).Should().BeFalse();
    }
}
