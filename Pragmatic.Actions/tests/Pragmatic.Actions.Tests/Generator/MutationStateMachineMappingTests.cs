using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     PRAG0434: a mutation that auto-maps the property an entity governs with a state machine.
/// </summary>
/// <remarks>
///     <para>
///         Auto-mapping emits <c>SetStatus(this.Status)</c>. That assigns the state and never asks
///         whether the move is legal, so the transitions declared with <c>[TransitionFrom]</c> hold
///         everywhere except at the one place the state actually changes — and the entity's own rule is
///         enforced by nobody.
///     </para>
///     <para>
///         Reported as a warning, not refused: assigning is occasionally what is meant — a migration, an
///         administrative repair — and those say so by suppressing it.
///     </para>
/// </remarks>
public class MutationStateMachineMappingTests : ActionsGeneratorTestBase
{
    private static string Source(string mutationBody) => """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.StateMachine;
        using Pragmatic.Result;

        namespace TestApp;

        public enum OrderStatus
        {
            [InitialState]
            Draft,
            [TransitionFrom(OrderStatus.Draft)]
            Placed
        }

        [Entity]
        [StateMachine<OrderStatus>]
        public partial class Order : IEntity
        {
            public Guid PersistenceId { get; set; }
            public OrderStatus Status { get; private set; }
            public string Reference { get; private set; } = "";

            internal void SetStatus(OrderStatus value) => Status = value;
            internal void SetReference(string value) => Reference = value;
        }

        """ + mutationBody;

    private const string MapsTheState = """
        [Mutation(Mode = MutationMode.Update)]
        public partial class PlaceOrder : Mutation<Order>
        {
            public required Guid Id { get; init; }
            public required OrderStatus Status { get; init; }
        }
        """;

    private const string LeavesTheStateAlone = """
        [Mutation(Mode = MutationMode.Update)]
        public partial class RenameOrder : Mutation<Order>
        {
            public required Guid Id { get; init; }
            public required string Reference { get; init; }
        }
        """;

    [Fact]
    public void MappingTheGovernedProperty_ReportsPRAG0434()
    {
        HasDiagnostic(RunGeneratorWithEntities(Source(MapsTheState)), "PRAG0434").Should().BeTrue();
    }

    [Fact]
    public void MappingSomethingElse_ReportsNothing()
    {
        // The guard against a diagnostic that fires on every mutation of a state-machine entity: only
        // the property the machine governs is the problem.
        HasDiagnostic(RunGeneratorWithEntities(Source(LeavesTheStateAlone)), "PRAG0434").Should().BeFalse();
    }

    [Fact]
    public void AnEntityWithNoStateMachine_ReportsNothing()
    {
        var plain = """
            #nullable enable
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            public partial class Ticket : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Status { get; private set; } = "";
                internal void SetStatus(string value) => Status = value;
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class RetitleTicket : Mutation<Ticket>
            {
                public required Guid Id { get; init; }
                public required string Status { get; init; }
            }
            """;

        HasDiagnostic(RunGeneratorWithEntities(plain), "PRAG0434").Should().BeFalse();
    }
}
