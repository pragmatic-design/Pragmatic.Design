using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Validation;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Validation;

/// <summary>
///     Tests for StateMachineValidator — compile-time state machine graph validation.
/// </summary>
public class StateMachineValidatorTests
{
    // =========================================================================
    // PRAG0620 — Missing initial state
    // =========================================================================

    [Fact]
    public void Validate_MissingInitialState_ReportsPRAG0620()
    {
        var model = new StateMachineModel
        {
            EntityTypeName = "Order",
            EntityFullTypeName = "TestApp.Order",
            Namespace = "TestApp",
            Accessibility = "public",
            EnumFullTypeName = "TestApp.OrderStatus",
            PropertyName = "Status",
            PropertyExists = true,
            InitialStateName = null, // no initial state
            IsValid = true,
            States = ImmutableArray.Create(
                new StateMachineStateModel { Name = "Draft" },
                new StateMachineStateModel
                {
                    Name = "Submitted", TransitionFromStates = ImmutableArray.Create("Draft")
                }
            )
        };

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().Contain(d => d.Id == "PRAG0620");
    }

    [Fact]
    public void Validate_WithInitialState_NoPRAG0620()
    {
        var model = CreateValidModel();

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0620");
    }

    // =========================================================================
    // PRAG0638 — More than one initial state
    // =========================================================================

    /// <summary>Two <c>[InitialState]</c> values are a contradiction, and it is reported.</summary>
    /// <remarks>
    ///     <c>StateMachineTransform</c> resolves the initial state with <c>FirstOrDefault</c>, so a
    ///     second declaration was silently discarded and the winner was whichever the enum declares
    ///     first — an order the author never chose as meaningful. Nothing said so: the validator only
    ///     ever looked at the zero case.
    /// </remarks>
    [Fact]
    public void Validate_TwoInitialStates_ReportsPRAG0638()
    {
        var model = CreateValidModel() with
        {
            InitialStateName = "Pending",
            States = ImmutableArray.Create(
                new StateMachineStateModel { Name = "Pending", IsInitial = true },
                new StateMachineStateModel { Name = "Confirmed", IsInitial = true },
                new StateMachineStateModel
                {
                    Name = "Cancelled", TransitionFromStates = ImmutableArray.Create("Pending")
                }
            )
        };

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().Contain(d => d.Id == "PRAG0638");
        diagnostics.First(d => d.Id == "PRAG0638").Severity.Should().Be(DiagnosticSeverity.Error,
            "the machine has no defined entry state, and the generator picks one by declaration order");
        diagnostics.First(d => d.Id == "PRAG0638").GetMessage()
            .Should().Contain("Pending").And.Contain("Confirmed",
                "the message has to name the values that disagree");
    }

    /// <summary>The control: exactly one initial state reports nothing.</summary>
    [Fact]
    public void Validate_OneInitialState_NoPRAG0638()
    {
        var diagnostics = StateMachineValidator.Validate(CreateValidModel(), Location.None);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0638");
    }

    // =========================================================================
    // PRAG0621 — Unreachable state
    // =========================================================================

    [Fact]
    public void Validate_UnreachableState_ReportsPRAG0621()
    {
        var model = new StateMachineModel
        {
            EntityTypeName = "Order",
            EntityFullTypeName = "TestApp.Order",
            Namespace = "TestApp",
            Accessibility = "public",
            EnumFullTypeName = "TestApp.OrderStatus",
            PropertyName = "Status",
            PropertyExists = true,
            InitialStateName = "Draft",
            IsValid = true,
            States = ImmutableArray.Create(
                new StateMachineStateModel { Name = "Draft", IsInitial = true },
                new StateMachineStateModel
                {
                    Name = "Submitted", TransitionFromStates = ImmutableArray.Create("Draft")
                },
                new StateMachineStateModel { Name = "Orphan" } // no incoming, not initial
            )
        };

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().Contain(d => d.Id == "PRAG0621");
        diagnostics.First(d => d.Id == "PRAG0621").GetMessage().Should().Contain("Orphan");
    }

    [Fact]
    public void Validate_AllReachable_NoPRAG0621()
    {
        var model = CreateValidModel();

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0621");
    }

    // =========================================================================
    // PRAG0622 — Invalid transition source
    // =========================================================================

    [Fact]
    public void Validate_InvalidTransitionSource_ReportsPRAG0622()
    {
        var model = new StateMachineModel
        {
            EntityTypeName = "Order",
            EntityFullTypeName = "TestApp.Order",
            Namespace = "TestApp",
            Accessibility = "public",
            EnumFullTypeName = "TestApp.OrderStatus",
            PropertyName = "Status",
            PropertyExists = true,
            InitialStateName = "Draft",
            IsValid = true,
            States = ImmutableArray.Create(
                new StateMachineStateModel { Name = "Draft", IsInitial = true },
                new StateMachineStateModel
                {
                    Name = "Submitted",
                    TransitionFromStates = ImmutableArray.Create("NonExistent") // invalid source
                }
            )
        };

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().Contain(d => d.Id == "PRAG0622");
        diagnostics.First(d => d.Id == "PRAG0622").GetMessage().Should().Contain("NonExistent");
    }

    [Fact]
    public void Validate_ValidTransitions_NoPRAG0622()
    {
        var model = CreateValidModel();

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0622");
    }

    // =========================================================================
    // PRAG0623 — the governed property is not on the entity
    // =========================================================================

    /// <summary>
    ///     The property name defaults to <c>Status</c>, so an entity whose state is called anything else
    ///     silently gets a machine generated against a member it does not have.
    /// </summary>
    /// <remarks>
    ///     What that looked like before this diagnostic: four <c>CS0103</c>s inside a generated file,
    ///     naming a property the author never wrote, with nothing to say that
    ///     <c>[StateMachine&lt;T&gt;(Property = ...)]</c> is the answer. Measured on a real entity whose
    ///     state is called <c>Outcome</c>.
    /// </remarks>
    [Fact]
    public void Validate_PropertyMissingFromEntity_ReportsPRAG0623()
    {
        var model = CreateValidModel() with { PropertyName = "Outcome", PropertyExists = false };

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().Contain(d => d.Id == "PRAG0623");
    }

    [Fact]
    public void Validate_PropertyMissing_TheMessageNamesItAndTheEntity()
    {
        // "A property does not exist" is not actionable in an entity with twenty of them.
        var model = CreateValidModel() with { PropertyName = "Outcome", PropertyExists = false };

        var message = StateMachineValidator.Validate(model, Location.None)
            .First(d => d.Id == "PRAG0623")
            .GetMessage();

        message.Should().Contain("Outcome").And.Contain("Reservation");
    }

    [Fact]
    public void Validate_PropertyPresent_NoPRAG0623()
        => StateMachineValidator.Validate(CreateValidModel(), Location.None)
            .Should().NotContain(d => d.Id == "PRAG0623");

    // =========================================================================
    // Complete valid model — no diagnostics
    // =========================================================================

    [Fact]
    public void Validate_ValidModel_NoDiagnostics()
    {
        var model = CreateValidModel();

        var diagnostics = StateMachineValidator.Validate(model, Location.None);

        diagnostics.Should().BeEmpty();
    }

    private static StateMachineModel CreateValidModel()
    {
        return new StateMachineModel
        {
            EntityTypeName = "Reservation",
            EntityFullTypeName = "TestApp.Reservation",
            Namespace = "TestApp",
            Accessibility = "public",
            EnumFullTypeName = "TestApp.ReservationStatus",
            PropertyName = "Status",
            PropertyExists = true,
            HasDomainEvents = false,
            InitialStateName = "Pending",
            IsValid = true,
            States = ImmutableArray.Create(
                new StateMachineStateModel { Name = "Pending", IsInitial = true },
                new StateMachineStateModel
                {
                    Name = "Confirmed", TransitionFromStates = ImmutableArray.Create("Pending")
                },
                new StateMachineStateModel
                {
                    Name = "CheckedIn", TransitionFromStates = ImmutableArray.Create("Confirmed")
                },
                new StateMachineStateModel
                {
                    Name = "Cancelled",
                    TransitionFromStates = ImmutableArray.Create("Pending", "Confirmed")
                }
            )
        };
    }
}
