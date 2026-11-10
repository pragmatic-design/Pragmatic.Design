using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.StateMachine;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Lifecycle;

/// <summary>
///     PRAG2754 — <c>[RaisesEvent&lt;T&gt;]</c> on a state of an entity that cannot raise events (#635).
/// </summary>
/// <remarks>
///     The generated <c>TransitionTo</c> writes the raise only when the entity derives from
///     <c>DomainEventSource</c>. On any other entity the attribute was accepted and the transition raised
///     nothing, with nothing said; PRAG2750 covered the class-level <c>[Raises&lt;T&gt;]</c> only.
/// </remarks>
public class ARaisedEventNeedsAnEventSourceTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<InitialStateAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.TryGetAssemblyReference("Pragmatic.Events")
            ?? throw new InvalidOperationException("Pragmatic.Events does not resolve in the test process.")
    ];

    private static SourceGenRunResult Run(string entityBase) =>
        GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.StateMachine;

            namespace Contoso.Sales;

            [Boundary]
            [Owns<Ticket>]
            public partial class SalesBoundary;

            public sealed record TicketEscalated(Guid TicketId, DateTimeOffset OccurredAt) : Pragmatic.Events.IDomainEvent;

            public enum TicketState
            {
                [InitialState]
                Open,

                [TransitionFrom(TicketState.Open)]
                [RaisesEvent<TicketEscalated>]
                Escalated
            }

            [Entity]
            [StateMachine<TicketState>]
            public partial class Ticket : {{entityBase}}
            {
                public TicketState Status { get; private set; }
            }
            """, References);

    [Fact]
    public void ARaisedEvent_OnAnEntityThatIsNotAnEventSource_IsReported()
    {
        var result = Run("IEntity");

        var reported = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2754").ToList();
        reported.Should().HaveCount(1, "the transition to Escalated would raise nothing, and nothing said so");
        reported[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should()
            .Contain("Escalated").And.Contain("TicketEscalated").And.Contain("derive from DomainEventSource");
    }

    /// <summary>
    ///     The control: on a <c>DomainEventSource</c> the raise is written, and nothing is reported.
    /// </summary>
    /// <remarks>Without it, reporting every <c>[RaisesEvent&lt;T&gt;]</c> would pass the test above.</remarks>
    [Fact]
    public void ARaisedEvent_OnAnEventSource_IsWrittenAndNotReported()
    {
        var result = Run("Pragmatic.Events.DomainEventSource, IEntity");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2754").Should().BeFalse();
        var machine = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(kv => kv.Key.EndsWith("Ticket.StateMachine.g.cs", StringComparison.Ordinal)).Value;
        machine.Should().Contain("RaiseEvent(new global::Contoso.Sales.TicketEscalated(");
    }
}
