using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for StateMachineTemplate which generates CanTransitionTo(),
///     TransitionTo(), and AllowedTransitions on entity partial classes.
/// </summary>
public class StateMachineTemplateTests
{
    [Fact]
    public void StateMachine_GeneratesCanTransitionTo()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("CanTransitionTo");
        source.Should().Contain("bool");
        source.Should().Contain("targetState");
    }

    [Fact]
    public void StateMachine_GeneratesTransitionTo()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("TransitionTo");
        source.Should().Contain("VoidResult<");
        source.Should().Contain("IError>");
    }

    [Fact]
    public void StateMachine_GeneratesAllowedTransitions()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("AllowedTransitions");
        source.Should().Contain("ReadOnlySpan");
    }

    [Fact]
    public void CanTransitionTo_ContainsTransitionTable()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Pending → Confirmed
        source.Should().Contain("Pending");
        source.Should().Contain("Confirmed");
        // Confirmed → CheckedIn
        source.Should().Contain("CheckedIn");
        // Default case
        source.Should().Contain("_ => false");
    }

    [Fact]
    public void TransitionTo_CallsSetterForChangeTracking()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should call SetStatus() which tracks in _modifiedProperties
        source.Should().Contain("SetStatus(targetState)");
    }

    [Fact]
    public void TransitionTo_ReturnsConflictErrorOnInvalidTransition()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("ConflictError");
        source.Should().Contain("EntityType");
        source.Should().Contain("Reservation");
        source.Should().Contain("Cannot transition");
    }

    [Fact]
    public void TransitionTo_ReturnsSuccessOnValidTransition()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("VoidResult<global::Pragmatic.Result.IError>.Success()");
    }

    [Fact]
    public void AllowedTransitions_GroupsBySourceState()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Pending can go to Confirmed and Cancelled
        source.Should().Contain("Pending =>");
        // Default empty
        source.Should().Contain("_ => []");
    }

    [Fact]
    public void StateMachine_WithEvents_GeneratesRaiseEvent()
    {
        // Arrange
        var model = CreateModelWithEvents();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("RaiseEvent");
        source.Should().Contain("ReservationConfirmedEvent");
    }

    [Fact]
    public void StateMachine_WithoutEvents_NoRaiseEvent()
    {
        // Arrange — model with HasDomainEvents but no events on states
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("RaiseEvent");
    }

    [Fact]
    public void StateMachine_WithoutDomainEventSupport_NoEventSwitch()
    {
        // Arrange — entity doesn't extend DomainEventSource
        var model = CreateReservationModel() with { HasDomainEvents = false };

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("RaiseEvent");
        source.Should().NotContain("switch (targetState)");
    }

    [Fact]
    public void StateMachine_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("partial class Reservation");
    }

    [Fact]
    public void StateMachine_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Entities;");
    }

    [Fact]
    public void StateMachine_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("Reservation");
        artifact.HintName.Should().Contain("StateMachine");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void StateMachine_EmptyStates_ReturnsEmptySource()
    {
        // Arrange
        var model = CreateReservationModel() with
        {
            States = ImmutableArray<StateMachineStateModel>.Empty
        };

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert — Validate() returns false → empty source
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void StateMachine_CustomPropertyName_UsesCustomSetter()
    {
        // Arrange — property is "Phase" instead of "Status"
        var model = CreateReservationModel() with
        {
            PropertyName = "Phase",
            PropertyExists = true,
            EnumFullTypeName = "MyApp.Entities.OrderPhase"
        };

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("SetPhase(targetState)");
        source.Should().Contain("Phase, targetState");
        source.Should().Contain("OrderPhase");
    }

    [Fact]
    public void StateMachine_MultipleTransitionSources_AllIncluded()
    {
        // Arrange — Cancelled can be reached from Pending AND Confirmed
        var model = CreateReservationModel();

        // Act
        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Both source states for Cancelled
        source.Should().Contain("Pending");
        source.Should().Contain("Confirmed");
        source.Should().Contain("Cancelled");
    }

    [Fact]
    public void StateMachine_WithRaisedEvent_GeneratesEventWithResolvedCtorArgs()
    {
        // Arrange — a transition target that raises a parameterized event (ctor args resolved from members).
        var model = CreateReservationModel() with
        {
            States = ImmutableArray.Create(
                new StateMachineStateModel { Name = "Pending", IsInitial = true },
                new StateMachineStateModel
                {
                    Name = "Confirmed",
                    TransitionFromStates = ImmutableArray.Create("Pending"),
                    RaisedEvents = ImmutableArray.Create(new StateRaisedEvent
                    {
                        TypeName = "MyApp.Events.ReservationConfirmed",
                        CtorArguments = ImmutableArray.Create("Id", "GuestId", "global::System.DateTimeOffset.UtcNow")
                    })
                })
        };

        // Act
        var source = new StateMachineTemplate(model).RenderOutput().Text;

        // Assert — the event is constructed with the resolved arguments, not a parameterless ctor.
        source.Should().Contain(
            "RaiseEvent(new global::MyApp.Events.ReservationConfirmed(Id, GuestId, global::System.DateTimeOffset.UtcNow));");
    }

    // =========================================================================
    // Helper methods
    // =========================================================================

    private static StateMachineModel CreateReservationModel()
    {
        return new StateMachineModel
        {
            EntityTypeName = "Reservation",
            EntityFullTypeName = "MyApp.Entities.Reservation",
            Namespace = "MyApp.Entities",
            Accessibility = "public",
            EnumFullTypeName = "MyApp.Entities.ReservationStatus",
            PropertyName = "Status",
            PropertyExists = true,
            HasDomainEvents = true,
            InitialStateName = "Pending",
            IsValid = true,
            States = ImmutableArray.Create(
                new StateMachineStateModel
                {
                    Name = "Pending",
                    IsInitial = true
                },
                new StateMachineStateModel
                {
                    Name = "Confirmed",
                    TransitionFromStates = ImmutableArray.Create("Pending")
                },
                new StateMachineStateModel
                {
                    Name = "CheckedIn",
                    TransitionFromStates = ImmutableArray.Create("Confirmed")
                },
                new StateMachineStateModel
                {
                    Name = "Cancelled",
                    TransitionFromStates = ImmutableArray.Create("Pending", "Confirmed")
                })
        };
    }

    private static StateMachineModel CreateModelWithEvents()
    {
        return new StateMachineModel
        {
            EntityTypeName = "Reservation",
            EntityFullTypeName = "MyApp.Entities.Reservation",
            Namespace = "MyApp.Entities",
            Accessibility = "public",
            EnumFullTypeName = "MyApp.Entities.ReservationStatus",
            PropertyName = "Status",
            PropertyExists = true,
            HasDomainEvents = true,
            InitialStateName = "Pending",
            IsValid = true,
            States = ImmutableArray.Create(
                new StateMachineStateModel
                {
                    Name = "Pending",
                    IsInitial = true
                },
                new StateMachineStateModel
                {
                    Name = "Confirmed",
                    TransitionFromStates = ImmutableArray.Create("Pending"),
                    RaisedEvents = ImmutableArray.Create(new StateRaisedEvent
                    {
                        TypeName = "MyApp.Events.ReservationConfirmedEvent",
                        CtorArguments = ImmutableArray<string>.Empty
                    })
                },
                new StateMachineStateModel
                {
                    Name = "CheckedIn",
                    TransitionFromStates = ImmutableArray.Create("Confirmed"),
                    RaisedEvents = ImmutableArray.Create(new StateRaisedEvent
                    {
                        TypeName = "MyApp.Events.GuestCheckedInEvent",
                        CtorArguments = ImmutableArray<string>.Empty
                    })
                })
        };
    }
}
