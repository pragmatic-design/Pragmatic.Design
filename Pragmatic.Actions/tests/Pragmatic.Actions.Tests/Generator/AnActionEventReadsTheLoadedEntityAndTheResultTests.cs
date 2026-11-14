using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An event an action declares with <c>[Raises&lt;T&gt;]</c> is filled from the action's inputs, the entity it loaded and the value its
///     body returned (#737).
/// </summary>
/// <remarks>
///     <para>
///         It was filled from the inputs alone: a member only the loaded record or the body's result holds was <c>default</c>, with
///         <c>PRAG0433</c>, although the invoker holds both when it builds the event. A mutation's event already reads its entity.
///     </para>
///     <para>
///         A name found in more than one place takes the input, then the result, then the loaded entity: the input is what the caller
///         asked, the result what the body computed, the entity the record it acted on.
///     </para>
/// </remarks>
public class AnActionEventReadsTheLoadedEntityAndTheResultTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authoring;
        using Pragmatic.Events;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Shipping;

        public partial class Order : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Number { get; set; } = "";
            public decimal Total { get; set; }
            public string Carrier { get; set; } = "";
        }

        public sealed record Receipt(string Carrier, int Parcels);

        public sealed record OrderShipped(Guid OrderId, string Number, decimal Total, string Carrier, int Parcels, Guid ShippedBy,
            DateTimeOffset OccurredAt) : IDomainEvent;

        [DomainAction]
        [LoadEntity<Order>(nameof(OrderId))]
        [Raises<OrderShipped>]
        public partial class ShipOrderAction : DomainAction<Receipt>
        {
            public required Guid OrderId { get; init; }
            public required Guid ShippedBy { get; init; }

            public override Task<Result<Receipt, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<Receipt, IError>>(new Receipt("Post", 2));
        }
        """;

    [Fact]
    public void AMemberOfTheLoadedEntity_FillsTheEvent()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "ShipOrderAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("action._order.Number").And.Contain("action._order.Total");
    }

    [Fact]
    public void AMemberOfTheResult_FillsTheEvent_AndWinsOverTheEntitysOfTheSameName()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "ShipOrderAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("result.Parcels").And.Contain("result.Carrier");
        invoker.Should().NotContain("action._order.Carrier", "the body's result is what the action computed, and it names a carrier too");
    }

    [Fact]
    public void AnInput_StillWins()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "ShipOrderAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("action.OrderId").And.Contain("action.ShippedBy");
    }

    [Fact]
    public void EveryMemberFound_ReportsNothing_AndCompiles()
    {
        var result = RunGeneratorWithEntities(Source);

        HasDiagnostic(result, "PRAG0433").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse(string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));
    }

    /// <summary>The control: a member none of the three holds is still reported, and still passed default.</summary>
    [Fact]
    public void AMemberFoundNowhere_IsStillReported()
    {
        var result = RunGeneratorWithEntities(Source.Replace("Guid ShippedBy,\n", "Guid ShippedBy, string Warehouse,\n", StringComparison.Ordinal)
            .Replace("Guid ShippedBy,\r\n", "Guid ShippedBy, string Warehouse,\r\n", StringComparison.Ordinal));

        HasDiagnostic(result, "PRAG0433").Should().BeTrue("no input, result member or entity member is called Warehouse");
    }
}
