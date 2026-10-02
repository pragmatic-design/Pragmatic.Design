using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.StateMachine;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The generated factory assigns the value marked <c>[InitialState]</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Reading and validating it is not assigning it. <c>[InitialState]</c> is read by
///         <c>StateMachineTransform</c> into <c>StateMachineModel.InitialStateName</c> and validated by
///         <c>StateMachineValidator</c> — with <c>PRAG0620</c> saying the factory "cannot set a default
///         state" — and a template that does not use it leaves a new entity at the enum's numeric zero.
///     </para>
///     <para>
///         Zero hides it: an <c>[InitialState]</c> on the first declared value makes zero and the
///         intended state the same number. These cases mark
///         the <b>second</b> value on purpose, which is the only shape that can tell the mechanism from
///         the coincidence.
///     </para>
/// </remarks>
public class InitialStateIsAssignedTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<InitialStateAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static string Run(string entityDeclaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.StateMachine;

            namespace Contoso.Sales;

            [Boundary]
            [Owns<Ticket>]
            public partial class SalesBoundary;

            public enum TicketState
            {
                // Not the first value: zero is Closed, so an entity created without the assignment
                // starts closed, which is what the defect did.
                Closed,

                [InitialState]
                Open,

                [TransitionFrom(TicketState.Open)]
                Escalated
            }

            {{entityDeclaration}}
            """, References);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var hit = sources.FirstOrDefault(kv => kv.Key.Contains("Ticket.Create"));
        hit.Value.Should().NotBeNull("the generator emits a Create factory for every entity");
        return hit.Value!;
    }

    [Fact]
    public void TheFactory_AssignsTheInitialState()
    {
        var create = Run("""
            [Entity]
            [StateMachine<TicketState>]
            public partial class Ticket : IEntity
            {
                public TicketState Status { get; private set; }
                public string Subject { get; private set; } = "";
            }
            """);

        create.Should().Contain("Status = global::Contoso.Sales.TicketState.Open,",
            "the entry state is a declaration, not the enum's numeric zero");
    }

    /// <summary>The property named by <c>Property = …</c> is the one assigned.</summary>
    [Fact]
    public void TheFactory_AssignsTheStateMachinesOwnProperty()
    {
        var create = Run("""
            [Entity]
            [StateMachine<TicketState>(Property = "Phase")]
            public partial class Ticket : IEntity
            {
                public TicketState Phase { get; private set; }
            }
            """);

        create.Should().Contain("Phase = global::Contoso.Sales.TicketState.Open,");
        create.Should().NotContain("Status =");
    }

    /// <summary>
    ///     The control: an entity with no state machine gets no such assignment.
    /// </summary>
    /// <remarks>
    ///     Without it the assertions above would hold on a template that assigned something to every
    ///     entity it could find an enum on.
    /// </remarks>
    [Fact]
    public void WithoutAStateMachine_TheFactoryAssignsNoState()
    {
        var create = Run("""
            [Entity]
            public partial class Ticket : IEntity
            {
                public TicketState Status { get; private set; }
            }
            """);

        create.Should().NotContain("TicketState.Open");
    }

    /// <summary>
    ///     A <c>[DefaultValue]</c> on the same property wins, because two initializers for one member
    ///     do not compile.
    /// </summary>
    [Fact]
    public void ADefaultValueOnTheSameProperty_IsNotDuplicated()
    {
        var create = Run("""
            [Entity]
            [StateMachine<TicketState>]
            public partial class Ticket : IEntity
            {
                [DefaultValue(TicketState.Escalated)]
                public TicketState Status { get; private set; }
            }
            """);

        create.Split("Status =").Length.Should().Be(2,
            "exactly one initializer for the property, or the generated file does not compile");
        create.Should().NotContain("Status = global::Contoso.Sales.TicketState.Open,");
    }
}
