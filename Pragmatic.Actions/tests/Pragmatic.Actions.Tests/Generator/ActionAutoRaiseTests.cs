using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Verifies #4 auto-raise: an action annotated with <c>[Raises&lt;T&gt;]</c> makes the generated invoker
///     override <c>CollectRaisedEvents</c>, constructing the event by matching ctor parameters to the action's
///     input properties (the entity carries no behavior).
/// </summary>
public class ActionAutoRaiseTests : ActionsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Authoring;
        using Pragmatic.Events;
        using Pragmatic.Result;
        """;

    [Fact]
    public void Raises_OnResultAction_GeneratesCollectRaisedEvents_MatchedByName()
    {
        var source = Usings + """

            namespace TestApp.Pharmacy;

            public sealed record DrugDispensed(Guid DrugId, decimal Quantity, DateTimeOffset OccurredAt) : IDomainEvent;

            [DomainAction]
            [Raises<DrugDispensed>]
            public partial class DispenseDrug : DomainAction<string>
            {
                public required Guid DrugId { get; init; }
                public required decimal Quantity { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("ok"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "Invoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("CollectRaisedEvents(DispenseDrug action, string result)");
        invoker.Should().Contain(
            "new global::TestApp.Pharmacy.DrugDispensed(action.DrugId, action.Quantity, global::System.DateTimeOffset.UtcNow)");
    }

    [Fact]
    public void Raises_OnVoidAction_GeneratesParameterlessOverride()
    {
        var source = Usings + """

            namespace TestApp.Pharmacy;

            public sealed record StockDepleted(Guid DrugId, DateTimeOffset OccurredAt) : IDomainEvent;

            [DomainAction]
            [Raises<StockDepleted>]
            public partial class DepleteStock : VoidDomainAction
            {
                public required Guid DrugId { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invoker = GetGeneratedSource(result, "Invoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("CollectRaisedEvents(DepleteStock action)");
        invoker.Should().NotContain(", string result)");
    }

    [Fact]
    public void Raises_WithUnmatchedCtorParameter_EmitsDefaultWithComment()
    {
        var source = Usings + """

            namespace TestApp.Pharmacy;

            public sealed record DrugDispensed(Guid DrugId, decimal Quantity, string PharmacistName, DateTimeOffset OccurredAt) : IDomainEvent;

            [DomainAction]
            [Raises<DrugDispensed>]
            public partial class DispenseDrug : DomainAction<string>
            {
                public required Guid DrugId { get; init; }
                public required decimal Quantity { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("ok"));
            }
            """;

        var result = RunGenerator(source);

        var invoker = GetGeneratedSource(result, "Invoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("default /* unmatched: PharmacistName */");
    }

    [Fact]
    public void NoRaises_DoesNotGenerateCollectRaisedEvents()
    {
        var source = Usings + """

            namespace TestApp.Pharmacy;

            [DomainAction]
            public partial class DispenseDrug : DomainAction<string>
            {
                public required Guid DrugId { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("ok"));
            }
            """;

        var result = RunGenerator(source);

        var invoker = GetGeneratedSource(result, "Invoker");
        invoker.Should().NotBeNull();
        invoker!.Should().NotContain("CollectRaisedEvents");
    }
}
