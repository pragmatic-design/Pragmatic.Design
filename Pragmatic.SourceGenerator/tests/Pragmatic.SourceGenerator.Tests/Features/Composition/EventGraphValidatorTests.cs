using Pragmatic.Testing.Assertions;
using Pragmatic.Events;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Composition.Validation;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     Verifies the host-level dangling-event detection (#6a): an event declared raised via
///     <c>[Raises&lt;T&gt;]</c> with no handler anywhere is reported; an event with a handler is not.
/// </summary>
public class EventGraphValidatorTests
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Authoring;
        using Pragmatic.Events;

        namespace TestApp;

        public sealed record DrugDispensed(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;
        public sealed record DrugRecalled(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;

        public class DispenseDrugAction
        {
            [Raises<DrugDispensed>]
            public void Execute() { }
        }

        public class RecallDrugAction
        {
            [Raises<DrugRecalled>]
            public void Execute() { }
        }

        // Handler only for DrugDispensed → DrugRecalled is dangling.
        public class DrugDispensedHandler : IDomainEventHandler<DrugDispensed>
        {
            public Task HandleAsync(DrugDispensed @event, CancellationToken ct = default) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void ComputeDangling_RaisedEventWithoutHandler_IsReported()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source, GeneratorTestHelper.FromType<IDomainEvent>());

        var dangling = EventGraphValidator.ComputeDangling(result.OutputCompilation, default);

        dangling.Should().ContainSingle()
            .Which.EventDisplayName.Should().Contain("DrugRecalled");
    }

    [Fact]
    public void ComputeDangling_RaisedEventWithHandler_IsNotReported()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source, GeneratorTestHelper.FromType<IDomainEvent>());

        var dangling = EventGraphValidator.ComputeDangling(result.OutputCompilation, default);

        dangling.Should().NotContain(d => d.EventDisplayName.Contains("DrugDispensed"));
    }

    [Fact]
    public void ComputeDangling_OriginPointsToRaiseSite()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source, GeneratorTestHelper.FromType<IDomainEvent>());

        var dangling = EventGraphValidator.ComputeDangling(result.OutputCompilation, default);

        dangling.Should().ContainSingle()
            .Which.Origin.Should().Be("RecallDrugAction.Execute()");
    }
}
